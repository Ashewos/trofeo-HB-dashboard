using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trofeo.Control;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.ViewModels.Fields;

/// <summary>
/// Une ligne proposée : un résultat du service, ou le choix du vide (« Pas de météo »). On montre le libellé du service,
/// on garde sa valeur telle quelle.
/// </summary>
public sealed partial class LookupRow(string label, JsonElement? value, bool isEmptyChoice, LookupFieldViewModel owner) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>La valeur du service ; <c>null</c> pour le choix du vide.</summary>
    public JsonElement? Value { get; } = value;

    public bool IsEmptyChoice { get; } = isEmptyChoice;

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private Task Pick() => owner.PickAsync(this);
}

/// <summary>
/// Un choix tiré d'une recherche du service. Cherchable : une saisie lance la recherche 400 ms après la dernière frappe,
/// avec 5 résultats au plus. Sinon : la liste se charge d'elle-même une fois, quand le champ s'affiche, et reste ouverte
/// (un bouton la relance si elle est vide). Le libellé du vide (<c>EmptyLabel</c>) remet la valeur à vide : la clé est retirée.
/// </summary>
public sealed partial class LookupFieldViewModel : FieldViewModel
{
    private const int MaxResults = 5;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

    private readonly LookupField _lookup;
    private CancellationTokenSource? _search;
    private Task? _loadTask;
    private List<LookupResult> _results = [];
    private bool _loaded;
    private bool _quiet;
    private string _query = "";

    public LookupFieldViewModel(LookupField field, IFieldContext context) : base(field, context)
    {
        _lookup = field;
        Refresh();
    }

    public bool IsSearchable => _lookup.Searchable;
    public string? Placeholder => _lookup.Placeholder;
    public string? EmptyLabel => _lookup.EmptyLabel;

    /// <summary>Les lignes proposées : le choix du vide d'abord (s'il existe), puis les résultats.</summary>
    public ObservableCollection<LookupRow> Rows { get; } = [];

    /// <summary>La liste est ouverte (résultats d'une recherche, ou liste chargée d'un champ non cherchable).</summary>
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isLoading;

    // ---- La valeur en vigueur ----

    /// <summary>La valeur est présente dans les réglages.</summary>
    public bool HasValue => StoredLabel() is not null;

    /// <summary>
    /// Ce qui nomme la valeur en vigueur : la chaîne elle-même, ou le champ <c>LabelPath</c> d'un objet. Sans valeur : le
    /// libellé du vide, ou rien.
    /// </summary>
    public string CurrentText => StoredLabel() ?? EmptyLabel ?? "";

    private string? StoredLabel()
    {
        var stored = Stored();
        return stored?.ValueKind switch
        {
            JsonValueKind.String => stored.Value.GetString(),
            JsonValueKind.Object => stored.Value.TryGetProperty(_lookup.LabelPath, out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : stored.Value.GetRawText(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => stored.Value.GetRawText(),
            _ => null,
        };
    }

    private JsonElement? Stored() => SettingsDocument.Get(Context.Settings, Key) is { ValueKind: not JsonValueKind.Null } value ? value : null;

    public override void Refresh()
    {
        OnPropertyChanged(nameof(CurrentText));
        OnPropertyChanged(nameof(HasValue));
        MarkSelected();
    }

    private void MarkSelected()
    {
        var stored = Stored();
        foreach (var row in Rows)
            row.IsSelected = row.IsEmptyChoice ? stored is null : stored is { } s && row.Value is { } v && JsonElement.DeepEquals(s, v);
    }

    // ---- La recherche ----

    /// <summary>Ce que la personne tape (champ cherchable). La changer relance la recherche, 400 ms après.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value ?? ""))
                return;
            if (IsSearchable && !_quiet)
                Schedule();
        }
    }

    /// <summary>La recherche en cours ou la dernière ; les tests l'attendent.</summary>
    public Task SearchTask { get; private set; } = Task.CompletedTask;

    private void Schedule()
    {
        _search?.Cancel();
        var source = new CancellationTokenSource();
        _search = source;
        SearchTask = SearchAsync(_query.Trim(), source.Token);
    }

    private async Task SearchAsync(string query, CancellationToken ct)
    {
        if (query.Length == 0)
        {
            _results = [];
            Error = null;
            IsLoading = false;
            Close();
            return;
        }

        try
        {
            await Task.Delay(Debounce, Context.Time, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return; // une frappe plus récente a pris le relais
        }

        if (await RunAsync(query, ct).ConfigureAwait(true) is { } results)
        {
            _results = [.. results.Take(MaxResults)];
            Open();
        }
    }

    /// <summary>Demande au service ; <c>null</c> si la demande a échoué (l'erreur est sous le champ) ou si une plus récente l'a remplacée.</summary>
    private async Task<IReadOnlyList<LookupResult>?> RunAsync(string query, CancellationToken ct)
    {
        IsLoading = true;
        Error = null;
        try
        {
            var results = await Context.LookupAsync(Key, query).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
                return null; // la réponse arrive après une frappe plus récente : elle ne vaut plus rien
            IsLoading = false;
            return results;
        }
        catch (Exception ex) when (ex is ControlException or TimeoutException)
        {
            if (!ct.IsCancellationRequested)
            {
                Error = string.Format(Texts.ModulesLookupFailed, ex.Message);
                IsLoading = false;
            }

            return null;
        }
    }

    // ---- La liste d'un champ non cherchable ----

    protected override void OnShown()
    {
        if (!IsSearchable)
            _ = EnsureLoadedAsync();
    }

    /// <summary>Charge la liste une fois (les appels suivants rendent le même chargement).</summary>
    public Task EnsureLoadedAsync() => _loadTask ??= LoadAsync();

    /// <summary>Relance le chargement (le bouton « Actualiser »).</summary>
    [RelayCommand]
    public Task ReloadAsync() => _loadTask = LoadAsync();

    /// <summary>La liste est vide et chargée : le bouton « Actualiser » se propose.</summary>
    public bool CanRefresh => !IsSearchable && _loaded && _results.Count == 0 && !IsLoading;

    private async Task LoadAsync()
    {
        _search?.Cancel();
        var source = new CancellationTokenSource();
        _search = source;
        var results = await RunAsync("", source.Token).ConfigureAwait(true);
        if (source.IsCancellationRequested)
            return; // un chargement plus récent a pris le relais
        _results = results is null ? [] : [.. results];
        _loaded = true;
        OnPropertyChanged(nameof(CanRefresh));
        Open();
    }

    // ---- Les lignes ----

    public void Open()
    {
        IsOpen = true;
        Rebuild();
    }

    public void Close()
    {
        IsOpen = false;
        Rebuild();
    }

    private void Rebuild()
    {
        Rows.Clear();
        if (IsOpen)
        {
            if (EmptyLabel is not null)
                Rows.Add(new LookupRow(EmptyLabel, null, isEmptyChoice: true, this));
            foreach (var result in _results)
                Rows.Add(new LookupRow(result.Label, result.Value, isEmptyChoice: false, this));
        }

        MarkSelected();
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(HasNoResult));
        OnPropertyChanged(nameof(HasNothingToOffer));
    }

    /// <summary>La recherche a répondu et n'a rien trouvé.</summary>
    public bool HasNoResult => IsSearchable && IsOpen && _results.Count == 0 && _query.Trim().Length > 0 && !IsLoading && !HasError;

    /// <summary>La liste à choisir est chargée et vide.</summary>
    public bool HasNothingToOffer => !IsSearchable && _loaded && _results.Count == 0 && !HasError;

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(HasNoResult));
    }

    protected override void OnErrorChanged()
    {
        OnPropertyChanged(nameof(HasNoResult));
        OnPropertyChanged(nameof(HasNothingToOffer));
    }

    public async Task PickAsync(LookupRow row)
    {
        Error = null;
        JsonElement? value = row.IsEmptyChoice ? null : row.Value;
        await EditAsync(settings => SettingsDocument.Set(settings, Key, value)).ConfigureAwait(true);

        if (IsSearchable)
        {
            _search?.Cancel();
            _quiet = true;
            try
            {
                Query = "";
            }
            finally
            {
                _quiet = false;
            }

            _results = [];
            Close();
        }

        Refresh();
    }
}
