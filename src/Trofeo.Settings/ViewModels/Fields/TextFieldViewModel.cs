using CommunityToolkit.Mvvm.ComponentModel;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.ViewModels.Fields;

/// <summary>
/// Un texte court. La frappe ne change que le brouillon : le texte s'enregistre à la perte du focus ou sur Entrée
/// (<see cref="CommitAsync"/>), en un seul pas d'annulation. Vide, il retire la clé. Une relecture des réglages ne
/// touche pas à un brouillon.
/// </summary>
public sealed partial class TextFieldViewModel : FieldViewModel
{
    private string _committed = "";

    public TextFieldViewModel(TextField field, IFieldContext context) : base(field, context)
    {
        MaxLength = field.MaxLength;
        Placeholder = field.Placeholder;
        _committed = Stored;
        _text = _committed;
    }

    public int MaxLength { get; }
    public string? Placeholder { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _text;

    /// <summary>Le brouillon n'est pas ce qui est écrit.</summary>
    public bool IsDirty => Text != _committed;

    private string Stored => SettingsDocument.GetString(Context.Settings, Key) ?? "";

    public async Task CommitAsync()
    {
        var value = Text.Trim();
        if (value.Length > MaxLength)
            value = value[..MaxLength].TrimEnd();
        if (value == _committed)
        {
            Text = _committed; // les espaces tapés en trop disparaissent
            return;
        }

        Error = null;
        await EditAsync(settings => SettingsDocument.Set(
            settings, Key, value.Length == 0 ? null : System.Text.Json.JsonSerializer.SerializeToElement(value))).ConfigureAwait(true);
        _committed = value;
        Text = value;
        Refresh();
    }

    public override Task CommitPendingAsync() => IsDirty ? CommitAsync() : Task.CompletedTask;

    public override void Refresh()
    {
        var stored = Stored;
        var wasDirty = IsDirty;
        _committed = stored;
        if (wasDirty)
            OnPropertyChanged(nameof(IsDirty)); // le brouillon reste, mais il est peut-être devenu la valeur écrite
        else
            Text = stored;
    }
}
