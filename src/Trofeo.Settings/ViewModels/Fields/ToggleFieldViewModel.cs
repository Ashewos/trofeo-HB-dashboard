using System.Text.Json;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.ViewModels.Fields;

/// <summary>Une case à deux états (un interrupteur). Elle s'enregistre tout de suite.</summary>
public sealed class ToggleFieldViewModel : FieldViewModel
{
    private readonly ToggleField _toggle;
    private bool _isOn;
    private bool _refreshing;

    public ToggleFieldViewModel(ToggleField field, IFieldContext context) : base(field, context)
    {
        _toggle = field;
        Refresh();
    }

    /// <summary>L'état montré par l'interrupteur (liaison à double sens) ; le changer l'enregistre.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value)
                return;
            _isOn = value;
            OnPropertyChanged();
            if (!_refreshing)
                Start(() => WriteAsync(value));
        }
    }

    public async Task SetAsync(bool value)
    {
        if (_isOn != value)
        {
            _isOn = value;
            OnPropertyChanged(nameof(IsOn));
        }

        await WriteAsync(value).ConfigureAwait(true);
    }

    private async Task WriteAsync(bool value)
    {
        if (Stored == value)
            return;
        Error = null;
        await EditAsync(settings => SettingsDocument.Set(settings, Key, JsonSerializer.SerializeToElement(value))).ConfigureAwait(true);
        Refresh();
    }

    private bool Stored => SettingsDocument.GetBool(Context.Settings, Key) ?? _toggle.Default;

    public override void Refresh()
    {
        var stored = Stored;
        if (_isOn == stored)
            return;
        _refreshing = true;
        try
        {
            _isOn = stored;
            OnPropertyChanged(nameof(IsOn));
        }
        finally
        {
            _refreshing = false;
        }
    }
}
