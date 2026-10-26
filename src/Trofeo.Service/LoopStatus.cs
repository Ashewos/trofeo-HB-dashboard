using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.ModuleKit;

namespace Trofeo.Display;

/// <summary>
/// Ce que la boucle d'affichage applique : la disposition, le thème complet et le fond. La boucle devient propriétaire
/// de <paramref name="Background"/> (elle le libère quand il est remplacé, ou à son arrêt).
/// </summary>
public sealed record LoopConfig(DashboardLayout Layout, ThemeDefinition Theme, IBackground Background);

/// <summary>Un module en place : son état et ses remarques du moment.</summary>
public sealed record LoopModule(Guid InstanceId, string TypeId, ModuleStatus Status, IReadOnlyList<string> Notices);

/// <summary>
/// L'état de la boucle, lisible depuis n'importe quel fil. <paramref name="Emulated"/> : pas d'écran, mais l'aperçu est
/// alimenté quand même. <paramref name="RenderMs"/> et <paramref name="SendMs"/> : durées récentes (moyenne glissante) de
/// rendu (composition et encodage) et d'envoi d'une image.
/// </summary>
public sealed record LoopStatus(bool Emulated, IReadOnlyList<LoopModule> Modules, int FramesSent, double RenderMs, double SendMs);
