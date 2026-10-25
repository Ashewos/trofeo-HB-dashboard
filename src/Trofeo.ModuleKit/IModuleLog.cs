namespace Trofeo.ModuleKit;

/// <summary>
/// Journal d'une instance de module : chaque message est préfixé du type et de l'instance par l'hôte.
/// Utilisable depuis n'importe quel thread.
/// </summary>
public interface IModuleLog
{
    void Info(string message);
    void Warning(string message);

    /// <summary>Un même message d'erreur n'est écrit en entier qu'une fois par minute.</summary>
    void Error(string message);
}
