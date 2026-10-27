using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.ViewModels.Fields;

/// <summary>Une option d'un choix : un bouton d'un groupe segmenté, ou une ligne de la liste.</summary>
public sealed partial class ChoiceOptionItem(string value, string label, ChoiceFieldViewModel owner) : ObservableObject
{
    public string Value { get; } = value;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private Task Select() => owner.SelectAsync(this);
}

/// <summary>Un choix parmi des options : boutons segmentés jusqu'à 3 options, liste au-delà. Il s'enregistre tout de suite.</summary>
public sealed class ChoiceFieldViewModel : FieldViewModel
{
    private const int MaxSegmented = 3;

    private readonly ChoiceField _choice;
    private ChoiceOptionItem? _selected;
    private bool _refreshing;

    public ChoiceFieldViewModel(ChoiceField field, IFieldContext context) : base(field, context)
    {
        _choice = field;
        Options = [.. field.Options.Select(o => new ChoiceOptionItem(o.Value, o.Label, this))];
        Refresh();
    }

    public IReadOnlyList<ChoiceOptionItem> Options { get; }

    public bool IsSegmented => Options.Count <= MaxSegmented;
    public bool IsDropDown => !IsSegmented;

    /// <summary>La valeur en vigueur : celle des réglages, sinon la valeur par défaut du schéma.</summary>
    public string SelectedValue => SettingsDocument.GetString(Context.Settings, Key) ?? _choice.Default;

    /// <summary>L'option choisie, pour la liste (liaison à double sens) ; <c>null</c> : la valeur écrite n'est pas une option.</summary>
    public ChoiceOptionItem? SelectedOption
    {
        get => _selected;
        set
        {
            if (_refreshing || value is null || ReferenceEquals(value, _selected))
                return;
            Start(() => SelectAsync(value));
        }
    }

    public async Task SelectAsync(ChoiceOptionItem option)
    {
        if (option.Value == SelectedValue)
            return;
        Error = null;
        await EditAsync(settings => SettingsDocument.Set(settings, Key, StringValue(option.Value))).ConfigureAwait(true);
        Refresh();
    }

    public override void Refresh()
    {
        var current = SelectedValue;
        foreach (var option in Options)
            option.IsSelected = option.Value == current;

        _refreshing = true;
        try
        {
            _selected = Options.FirstOrDefault(o => o.IsSelected);
            OnPropertyChanged(nameof(SelectedOption));
            OnPropertyChanged(nameof(SelectedValue));
        }
        finally
        {
            _refreshing = false;
        }
    }
}
