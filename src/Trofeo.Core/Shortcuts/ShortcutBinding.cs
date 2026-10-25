using Trofeo.ModuleKit;

namespace Trofeo.Core.Shortcuts;

/// <summary>Un raccourci clavier global et la commande qu'il envoie à un module.</summary>
/// <param name="Keys">Les touches, par exemple « Ctrl+Alt+Right » (voir <see cref="ShortcutKeys.TryParse"/>).</param>
/// <param name="Command">Ce qui est demandé au module.</param>
/// <param name="Index">Pour <see cref="ModuleCommandKind.SelectTab"/> : rang de l'onglet, à partir de 0.</param>
/// <param name="Target">Instance visée ; <c>null</c> : le premier module qui accepte les commandes.</param>
public sealed record ShortcutBinding(string Keys, ModuleCommandKind Command, int Index = 0, Guid? Target = null);
