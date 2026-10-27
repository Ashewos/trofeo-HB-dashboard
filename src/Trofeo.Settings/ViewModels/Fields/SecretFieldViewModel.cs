using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trofeo.ModuleKit;

namespace Trofeo.Settings.ViewModels.Fields;

/// <summary>
/// Un secret du coffre (sa clé est dans la portée du type de module, jamais dans les réglages). Le champ montre seulement
/// « Enregistrée » ou « Absente » : la valeur ne revient jamais à l'écran, ni dans un message. La saisie est masquée et
/// effacée dès qu'elle est partie dans le coffre. Ensuite les modules placés sont rechargés pour relire le coffre.
/// </summary>
public sealed partial class SecretFieldViewModel : FieldViewModel
{
    public SecretFieldViewModel(SecretField field, IFieldContext context) : base(field, context) => _isPresent = ReadPresence();

    /// <summary>
    /// Demande au coffre si la clé y est. Le coffre prend un verrou entre processus (10 s au plus) et peut échouer : jamais
    /// d'exception ici, la clé est alors montrée absente et l'erreur s'affiche sous le champ.
    /// </summary>
    private bool ReadPresence()
    {
        try
        {
            return Context.Secrets.Has(Context.TypeId, Key);
        }
        catch (Exception ex)
        {
            Error = string.Format(Texts.ModulesSecretUnreadable, ex.Message);
            return false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(CanClear))]
    private bool _isPresent;

    /// <summary>La saisie est ouverte (« Modifier » a été demandé).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    private bool _isEditing;

    public bool IsNotEditing => !IsEditing;

    /// <summary>La valeur tapée, masquée à l'écran ; vide dès qu'elle est enregistrée ou abandonnée.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _input = "";

    public string StatusText => IsPresent ? Texts.ModulesSecretPresent : Texts.ModulesSecretAbsent;

    public bool CanSave => Input.Trim().Length > 0;
    public bool CanClear => IsPresent;

    [RelayCommand]
    public void Edit()
    {
        Error = null;
        IsEditing = true;
    }

    /// <summary>« Annuler » : rien n'est écrit, la saisie est effacée.</summary>
    [RelayCommand]
    public void Cancel()
    {
        Input = "";
        Error = null;
        IsEditing = false;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (!CanSave)
            return;
        if (!Write(Input.Trim(), Texts.ModulesSecretSaveFailed))
            return;
        Input = "";
        IsEditing = false;
        await Context.SecretChangedAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task ClearAsync()
    {
        if (!Write(null, Texts.ModulesSecretClearFailed))
            return;
        Input = "";
        IsEditing = false;
        await Context.SecretChangedAsync().ConfigureAwait(true);
    }

    /// <summary>Écrit dans le coffre ; faux (et l'erreur sous le champ) s'il est verrouillé ou illisible.</summary>
    private bool Write(string? value, string failure)
    {
        Error = null;
        try
        {
            Context.Secrets.Set(Context.TypeId, Key, value);
        }
        catch (Exception ex)
        {
            Error = string.Format(failure, ex.Message); // le message de l'exception ne porte pas la valeur
            return false;
        }

        // L'écriture a réussi : on sait ce que le coffre contient, même s'il ne répond plus. Le module doit être rechargé quand même.
        IsPresent = value is not null;
        return true;
    }
}
