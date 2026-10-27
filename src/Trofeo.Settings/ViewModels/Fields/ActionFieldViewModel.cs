using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trofeo.Control;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.ViewModels.Fields;

/// <summary>
/// Un bouton qui lance une action du module (par le service), avec sa confirmation si le schéma en demande une. Pendant
/// l'action, le bouton est inactif et dit « En cours… » ; ensuite le message de l'action s'affiche. Le service recrée
/// lui-même l'instance après une action réussie : la fenêtre n'ajoute rien.
/// </summary>
public sealed partial class ActionFieldViewModel : FieldViewModel
{
    private readonly ActionField _action;
    private readonly Func<Task> _prepare;
    private bool _ok;

    /// <param name="prepare">Appelé juste avant l'action : écrit ce qui est encore en brouillon dans le formulaire.</param>
    public ActionFieldViewModel(ActionField field, IFieldContext context, Func<Task>? prepare = null) : base(field, context)
    {
        _action = field;
        _prepare = prepare ?? (() => Task.CompletedTask);
    }

    public string ButtonText => IsRunning ? Texts.ModulesActionRunning : Label;

    /// <summary>La question posée avant de lancer l'action ; <c>null</c> : pas de confirmation.</summary>
    public string? ConfirmText => _action.Confirm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText), nameof(CanRun))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotConfirming))]
    private bool _isConfirming;

    public bool IsNotConfirming => !IsConfirming;

    /// <summary>Le message de la dernière action ; <c>null</c> : rien.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage), nameof(IsOk), nameof(IsFailed))]
    private string? _message;

    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public bool IsOk => HasMessage && _ok;
    public bool IsFailed => HasMessage && !_ok;

    /// <summary>Le bouton est actif : rien ne tourne, et le module est sur l'écran (le service ne fait pas tourner un module retiré).</summary>
    public bool CanRun => !IsRunning && Context.CanRunActions;

    /// <summary>Le module est retiré de l'écran : l'action attend qu'on le remette.</summary>
    public bool IsUnavailable => !Context.CanRunActions;

    public override void Refresh()
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(IsUnavailable));
    }

    /// <summary>Le clic sur le bouton : la question d'abord s'il y en a une, sinon l'action.</summary>
    [RelayCommand]
    public async Task RequestAsync()
    {
        if (!CanRun)
            return;
        Message = null;
        if (_action.Confirm is not null)
        {
            IsConfirming = true;
            return;
        }

        await RunAsync().ConfigureAwait(true);
    }

    /// <summary>La réponse « oui » à la question.</summary>
    [RelayCommand]
    public async Task ConfirmAsync()
    {
        if (!IsConfirming)
            return;
        IsConfirming = false;
        if (CanRun)
            await RunAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public void CancelConfirm() => IsConfirming = false;

    private async Task RunAsync()
    {
        IsRunning = true;
        try
        {
            await _prepare().ConfigureAwait(true);
            var result = await Context.RunActionAsync(Key).ConfigureAwait(true);
            _ok = result.Ok;
            Message = result.Message;
        }
        catch (Exception ex) when (ex is ControlException or TimeoutException)
        {
            _ok = false;
            Message = ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }
}
