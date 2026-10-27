using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.ViewModels.Fields;

/// <summary>
/// Un champ du formulaire d'un module. Il lit sa valeur dans les réglages du contexte (<see cref="Refresh"/> la relit
/// quand ils changent) et l'écrit par <see cref="IFieldContext.EditAsync"/>. Tout se passe sur le fil de l'interface.
/// </summary>
public abstract class FieldViewModel : ObservableObject
{
    private bool _isVisible;
    private string? _error;

    protected FieldViewModel(SettingField field, IFieldContext context)
    {
        Field = field;
        Context = context;
    }

    public SettingField Field { get; }

    protected IFieldContext Context { get; }

    public string Key => Field.Key;
    public string Label => Field.Label;
    public string? Help => Field.Help;
    public bool HasHelp => !string.IsNullOrEmpty(Field.Help);

    /// <summary>Le champ s'affiche (sa condition <c>VisibleWhen</c> est remplie). Le formulaire le recalcule à chaque changement.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value) && value)
                OnShown();
        }
    }

    /// <summary>Ce qui a mal tourné pour ce champ (en <c>Refuse</c> sous lui) ; <c>null</c> : rien.</summary>
    public string? Error
    {
        get => _error;
        protected set
        {
            if (!SetProperty(ref _error, value))
                return;
            OnPropertyChanged(nameof(HasError));
            OnErrorChanged();
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    /// <summary>La dernière écriture lancée par un changement de la vue (une case cochée) ; les tests l'attendent.</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>Relit la valeur dans les réglages du contexte.</summary>
    public virtual void Refresh()
    {
    }

    /// <summary>Écrit ce qui n'est pas encore écrit (un texte tapé, pas encore validé).</summary>
    public virtual Task CommitPendingAsync() => Task.CompletedTask;

    protected virtual void OnErrorChanged()
    {
    }

    /// <summary>Le champ vient d'apparaître (au premier affichage du formulaire, ou quand sa condition devient vraie).</summary>
    protected virtual void OnShown()
    {
    }

    /// <summary>Lance une écriture déclenchée par la vue, sans l'attendre : une erreur imprévue s'affiche sous le champ.</summary>
    protected void Start(Func<Task> work) => Pending = Observe(work);

    private async Task Observe(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    protected Task EditAsync(Func<JsonElement, JsonElement> change) => Context.EditAsync(change);

    protected static JsonElement StringValue(string value) => JsonSerializer.SerializeToElement(value);
}
