using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.ViewModels.Fields;

/// <summary>Une ligne de la liste : une case à cocher, et des flèches pour monter ou descendre si elle est cochée.</summary>
public sealed partial class ListEntry(string value, string label, ListFieldViewModel owner) : ObservableObject
{
    private bool _isChecked;

    public string Value { get; } = value;
    public string Label { get; } = label;

    /// <summary>Liaison à double sens avec la case ; la changer à la main enregistre.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;
            owner.OnCheckedByUser(this, value);
            OnPropertyChanged(); // la case se remet d'aplomb si la liste a refusé
        }
    }

    [ObservableProperty] private bool _canMoveUp;
    [ObservableProperty] private bool _canMoveDown;

    /// <summary>La case peut se décocher : il reste toujours une ligne cochée.</summary>
    [ObservableProperty] private bool _canUncheck = true;

    internal void SetChecked(bool value)
    {
        if (_isChecked == value)
            return;
        _isChecked = value;
        OnPropertyChanged(nameof(IsChecked));
    }

    [RelayCommand]
    private Task MoveUp() => owner.MoveAsync(this, -1);

    [RelayCommand]
    private Task MoveDown() => owner.MoveAsync(this, 1);
}

/// <summary>
/// Un sous-ensemble ordonné des options, sans doublon : les lignes cochées d'abord, dans leur ordre, puis les autres. Une
/// case cochée ou une flèche enregistre tout de suite, un clic un enregistrement. Il reste toujours une ligne cochée : un
/// module sans ligne valide reprendrait ses lignes par défaut, et la liste mentirait.
/// </summary>
public sealed class ListFieldViewModel : FieldViewModel
{
    private readonly ListField _list;
    private readonly Dictionary<string, ListEntry> _byValue = [];
    private List<string> _order = [];

    public ListFieldViewModel(ListField field, IFieldContext context) : base(field, context)
    {
        _list = field;
        foreach (var option in field.Options)
        {
            if (!_byValue.ContainsKey(option.Value))
                _byValue[option.Value] = new ListEntry(option.Value, option.Label, this);
        }

        Refresh();
    }

    /// <summary>Toutes les options, une fois chacune : les cochées d'abord, dans l'ordre écrit.</summary>
    public ObservableCollection<ListEntry> Entries { get; } = [];

    /// <summary>Les valeurs cochées, dans l'ordre.</summary>
    public IReadOnlyList<string> Selected => _order;

    public override void Refresh()
    {
        var read = SettingsDocument.Get(Context.Settings, Key) is { ValueKind: JsonValueKind.Array }
            ? SettingsDocument.GetStrings(Context.Settings, Key)
            : _list.Default;
        var order = read.Where(_byValue.ContainsKey).Distinct().ToList();
        if (order.Count == 0)
            order = [.. _list.Default.Where(_byValue.ContainsKey).Distinct()];
        Apply(order);
    }

    internal void OnCheckedByUser(ListEntry entry, bool value) => Start(() => ToggleAsync(entry, value));

    public async Task ToggleAsync(ListEntry entry, bool on)
    {
        var order = _order.ToList();
        if (on)
        {
            if (order.Contains(entry.Value))
                return;
            order.Add(entry.Value);
        }
        else
        {
            if (!order.Contains(entry.Value) || order.Count == 1)
            {
                Apply(_order); // la case refusée se remet comme elle était
                return;
            }

            order.Remove(entry.Value);
        }

        await WriteAsync(order).ConfigureAwait(true);
    }

    public async Task MoveAsync(ListEntry entry, int delta)
    {
        var index = _order.IndexOf(entry.Value);
        var target = Math.Clamp(index + delta, 0, _order.Count - 1);
        if (index < 0 || target == index)
            return;

        var order = _order.ToList();
        order.RemoveAt(index);
        order.Insert(target, entry.Value);
        await WriteAsync(order).ConfigureAwait(true);
    }

    private async Task WriteAsync(List<string> order)
    {
        Error = null;
        Apply(order); // la liste suit le clic sans attendre le service
        var array = JsonSerializer.SerializeToElement(order);
        await EditAsync(settings => SettingsDocument.Set(settings, Key, array)).ConfigureAwait(true);
        Refresh();
    }

    /// <summary>Montre cet ordre : cochées d'abord, puis les autres dans l'ordre des options. Les lignes déjà là se déplacent, elles ne sont pas refaites.</summary>
    private void Apply(List<string> order)
    {
        _order = order;
        var desired = order.Select(v => _byValue[v])
            .Concat(_list.Options.Select(o => o.Value).Distinct().Where(v => !order.Contains(v)).Select(v => _byValue[v]))
            .ToList();

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < Entries.Count && ReferenceEquals(Entries[i], desired[i]))
                continue;
            var current = Entries.IndexOf(desired[i]);
            if (current < 0)
                Entries.Insert(i, desired[i]);
            else
                Entries.Move(current, i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var entry = desired[i];
            var position = order.IndexOf(entry.Value);
            entry.SetChecked(position >= 0);
            entry.CanMoveUp = position > 0;
            entry.CanMoveDown = position >= 0 && position < order.Count - 1;
            entry.CanUncheck = position < 0 || order.Count > 1;
        }
    }
}
