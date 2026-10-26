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

    /// <summary>La disposition lue ou écrite en dernier.</summary>
    public DashboardLayout Current { get; private set; } = DashboardLayout.Default;

    /// <summary>Pourquoi le fichier n'a pas pu être lu (illisible, invalide) ; <c>null</c> s'il l'a été.</summary>
    public string? LoadError { get; private set; }

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
    /// Compare l'empreinte au fichier lu ; s'il a changé : relit, lève <see cref="Replaced"/>, <c>Saved</c> faux avec le
    /// message « La disposition a changé en dehors des réglages ». Sinon : écrit, puis demande le rechargement au service.
    /// Un refus du service rend <c>Saved</c> vrai (le fichier est écrit), avec le motif en <c>Message</c> ; une liaison
    /// coupée aussi.
    /// </summary>
    public async Task<SaveOutcome> SaveAsync(DashboardLayout layout)
    {
        if (store.Stamp() != _stamp)
        {
            Load();
            Replaced?.Invoke();
            return new SaveOutcome(false, Texts.LayoutChangedOutside, []);
        }

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
