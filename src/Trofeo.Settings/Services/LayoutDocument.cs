using System.Text.Json;
using Trofeo.Control;
using Trofeo.Core.Layout;

namespace Trofeo.Settings.Services;

/// <summary>Le résultat d'un enregistrement : écrit ou non, avec le message à montrer et les avertissements du service.</summary>
public sealed record SaveOutcome(bool Saved, string? Message, IReadOnlyList<string> Warnings);

/// <summary>
/// La disposition telle que la fenêtre la montre. Chaque changement est écrit dans <c>layout.json</c>, puis le service est
/// prié de la recharger. Avant d'écrire, la fenêtre compare le fichier à celui qu'elle a lu : si quelqu'un d'autre l'a
/// modifié entre-temps (<c>HardwareCheck --appearance</c>, une édition à la main), elle le relit au lieu de l'écraser.
/// </summary>
public sealed class LayoutDocument(LayoutStore store, IServiceLink link)
{
    private LayoutStamp? _stamp;

    /// <summary>Le chemin de <c>layout.json</c>.</summary>
    public string FilePath => store.FilePath;

    /// <summary>La disposition lue ou écrite en dernier.</summary>
    public DashboardLayout Current { get; private set; } = DashboardLayout.Default;

    /// <summary>Pourquoi le fichier n'a pas pu être lu (illisible, invalide) ; <c>null</c> s'il l'a été.</summary>
    public string? LoadError { get; private set; }

    /// <summary>
    /// La ligne ambre de la fenêtre tant que <see cref="LoadError"/> est renseigné ; <c>null</c> sinon. Tant que le fichier ne se
    /// lit pas, la disposition montrée est celle par défaut et rien n'est écrit par-dessus.
    /// </summary>
    public string? LoadErrorMessage =>
        LoadError is { } reason ? string.Format(Texts.LayoutUnreadable, reason.TrimEnd().TrimEnd('.')) : null;

    /// <summary>Le fichier a été relu à la place d'une modification de la fenêtre.</summary>
    public event Action? Replaced;

    /// <summary>
    /// Relit le fichier. S'il est illisible ou invalide, la disposition en cours est gardée (la disposition par défaut au
    /// premier appel) et <see cref="LoadError"/> dit pourquoi : rien n'est copié en <c>.bad</c> ni remplacé.
    /// </summary>
    public void Load()
    {
        // L'empreinte est prise avant la lecture : un fichier réécrit entre les deux sera vu comme changé.
        _stamp = store.Stamp();
        var result = store.TryLoad();
        LoadError = result.Layout is null ? result.Error : null;
        if (result.Layout is not null)
            Current = result.Layout;
    }

    /// <summary>
    /// Si le fichier ne se lisait pas à l'ouverture, le relit : un verrou passager se résout tout seul. Levé
    /// <see cref="Replaced"/> s'il se lit maintenant (la disposition montrée n'était pas la sienne). Rend vrai s'il reste
    /// illisible.
    /// </summary>
    internal bool RetryLoad()
    {
        if (LoadError is null)
            return false;
        Load();
        if (LoadError is null)
            Replaced?.Invoke();
        return LoadError is not null;
    }

    /// <summary>
    /// Compare l'empreinte au fichier lu ; s'il a changé : relit, lève <see cref="Replaced"/>, <c>Saved</c> faux avec le
    /// message « La disposition a changé en dehors des réglages ». Un fichier absent à l'ouverture et présent maintenant n'a pas
    /// changé en dehors des réglages s'il dit la même chose que la disposition montrée (le service démarré depuis la
    /// fenêtre vient d'écrire la disposition par défaut). Tant que le fichier est illisible (<see cref="LoadError"/>), rien
    /// n'est écrit : il est relu une fois, et <c>Saved</c> reste faux. Sinon : écrit, puis demande le rechargement au service.
    /// Un refus du service rend <c>Saved</c> vrai (le fichier est écrit), avec le motif en <c>Message</c> ; une liaison
    /// coupée aussi.
    /// </summary>
    public async Task<SaveOutcome> SaveAsync(DashboardLayout layout)
    {
        if (LoadError is not null)
        {
            return RetryLoad()
                ? new SaveOutcome(false, LoadErrorMessage, [])
                : new SaveOutcome(false, Texts.LayoutChangedOutside, []); // la modification partait de la disposition par défaut
        }

        if (ChangedOutside())
        {
            Load();
            Replaced?.Invoke();
            return new SaveOutcome(false, Texts.LayoutChangedOutside, []);
        }

        return await WriteAsync(layout).ConfigureAwait(true);
    }

    /// <summary>
    /// Repart de la disposition par défaut quand le fichier est illisible : le fichier est d'abord copié en
    /// <c>layout.json.avant-reglages</c> (rien n'est écrit si la copie échoue), puis la disposition par défaut est écrite et le
    /// service prié de la recharger. Sans <see cref="LoadError"/>, ne fait rien. Si le fichier a changé depuis (corrigé à la main),
    /// il est relu à la place.
    /// </summary>
    public async Task<SaveOutcome> ResetToDefaultAsync()
    {
        if (LoadError is null)
            return new SaveOutcome(false, null, []);

        if (ChangedOutside())
        {
            Load();
            Replaced?.Invoke();
            return new SaveOutcome(false, Texts.LayoutChangedOutside, []);
        }

        try
        {
            if (File.Exists(store.FilePath))
                File.Copy(store.FilePath, store.FilePath + ".avant-reglages", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SaveOutcome(false, string.Format(Texts.SaveFailed, ex.Message), []);
        }

        return await WriteAsync(DashboardLayout.Default).ConfigureAwait(true);
    }

    /// <summary>Le fichier n'est plus celui que la fenêtre a lu (empreinte changée), sauf s'il dit la même chose qu'elle.</summary>
    private bool ChangedOutside()
    {
        var stamp = store.Stamp();
        if (stamp == _stamp)
            return false;

        // Absent à l'ouverture, présent maintenant : le service démarré depuis la fenêtre a écrit la disposition par défaut.
        if (_stamp is null && stamp is not null && store.TryLoad().Layout is { } found && SameContent(found, Current))
        {
            _stamp = stamp;
            return false;
        }

        return true;
    }

    private static bool SameContent(DashboardLayout a, DashboardLayout b) =>
        JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

    private async Task<SaveOutcome> WriteAsync(DashboardLayout layout)
    {
        try
        {
            store.Save(layout);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SaveOutcome(false, string.Format(Texts.SaveFailed, ex.Message), []);
        }

        Current = layout;
        LoadError = null;
        _stamp = store.Stamp();

        try
        {
            var reloaded = await link.ReloadAsync().ConfigureAwait(true);
            return new SaveOutcome(true, reloaded.Applied ? null : reloaded.Error ?? Texts.ReloadRefused, reloaded.Warnings);
        }
        catch (Exception ex) when (ex is ControlException or TimeoutException)
        {
            return new SaveOutcome(true, ex.Message, []);
        }
    }
}
