namespace Trofeo.ModuleKit;

/// <summary>Secrets d'un type de module (jetons, mots de passe), chiffrés au repos par l'hôte.</summary>
public interface ISecretStore
{
    /// <summary>La valeur de la clé, ou <c>null</c> si elle est absente.</summary>
    string? Get(string key);

    /// <summary>Enregistre la valeur ; <c>Set(key, null)</c> efface la clé.</summary>
    void Set(string key, string? value);
}
