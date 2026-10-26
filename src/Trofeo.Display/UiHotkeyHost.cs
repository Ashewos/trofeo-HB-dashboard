using System.Diagnostics;
using Trofeo.Core.Shortcuts;

namespace Trofeo.Display;

/// <summary>
/// Pose les raccourcis sur le fil de l'interface (la fenêtre des raccourcis y vit) pour le compte du rechargement, qui
/// tourne sur un autre fil. Attend au plus <see cref="Timeout"/> ; sans module qui accepte les commandes, aucun raccourci
/// n'est posé (il prendrait une combinaison à d'autres applications pour rien).
/// </summary>
internal sealed class UiHotkeyHost(SynchronizationContext ui, HotkeyRouter router, DisplayLoop loop) : IHotkeyHost
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<string>> ApplyAsync(IReadOnlyList<ShortcutBinding> bindings)
    {
        var done = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            ui.Post(_ =>
            {
                try
                {
                    IReadOnlyList<ShortcutBinding> wanted = loop.FindCommandTarget(null) is null ? [] : bindings;
                    done.TrySetResult(router.Replace(wanted));
                }
                catch (ObjectDisposedException)
                {
                    done.TrySetResult([]); // le service s'arrête : plus de raccourcis à poser
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Raccourcis non mis à jour : {ex.Message}");
                    done.TrySetResult([$"Les raccourcis n'ont pas pu être mis à jour : {ex.Message}"]);
                }
            }, null);
        }
        catch (InvalidOperationException)
        {
            return []; // la boucle d'interface est terminée
        }

        try
        {
            return await done.Task.WaitAsync(Timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return ["Les raccourcis n'ont pas pu être mis à jour : l'interface ne répond pas."];
        }
    }
}
