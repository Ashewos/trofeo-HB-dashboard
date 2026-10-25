namespace Trofeo.Modules.NowPlaying;

/// <summary>Source de ce qui est en lecture (les contrôles média de Windows en production, un faux en test).</summary>
public interface IMediaSource : IDisposable
{
    /// <summary>Ce qui est en lecture, ou <c>null</c> si rien. Remplacé d'un bloc ; lisible depuis n'importe quel thread.</summary>
    MediaSnapshot? Current { get; }

    /// <summary>Levé, depuis n'importe quel thread, après chaque remplacement de <see cref="Current"/>.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Se connecte à la source ; <see cref="Current"/> est à jour au retour. Une source qui peut réessayer seule
    /// (les contrôles média de Windows) ne lève pas si elle est momentanément muette : elle réessaie en tâche de fond
    /// et lève <see cref="Changed"/> à la connexion.
    /// </summary>
    Task StartAsync(CancellationToken ct);
}
