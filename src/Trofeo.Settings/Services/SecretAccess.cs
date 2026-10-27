using System.Runtime.Versioning;
using Trofeo.Core;
using Trofeo.Core.Secrets;

namespace Trofeo.Settings.Services;

/// <summary>
/// Ce que la fenêtre sait du coffre à secrets : si une clé y est, et l'écrire ou l'effacer. Elle ne lit jamais une valeur :
/// un champ secret montre seulement « Enregistrée » ou « Absente ». La portée est l'identifiant du type de module.
/// </summary>
public interface ISecretAccess
{
    bool Has(string typeId, string key);

    /// <summary>Enregistre la valeur ; <c>null</c> efface la clé. Lève si le coffre est verrouillé ou illisible.</summary>
    void Set(string typeId, string key, string? value);
}

/// <summary>Le coffre DPAPI du service (<c>secrets.dat</c>), qui a déjà un verrou entre processus.</summary>
[SupportedOSPlatform("windows")]
public sealed class VaultSecretAccess(DpapiSecretVault vault) : ISecretAccess
{
    /// <summary>Le coffre du dossier des réglages de l'utilisateur, celui que lit le service.</summary>
    public static VaultSecretAccess Default() => new(new DpapiSecretVault(Path.Combine(AppPaths.SettingsDir, "secrets.dat")));

    public bool Has(string typeId, string key) => !string.IsNullOrEmpty(vault.Get(typeId, key));

    public void Set(string typeId, string key, string? value) => vault.Set(typeId, key, value);
}

/// <summary>Pas de coffre (fenêtre montée sans lui) : aucune clé, et rien ne s'écrit.</summary>
internal sealed class NoSecretAccess : ISecretAccess
{
    public bool Has(string typeId, string key) => false;

    public void Set(string typeId, string key, string? value) => throw new InvalidOperationException(Texts.ModulesSecretUnavailable);
}
