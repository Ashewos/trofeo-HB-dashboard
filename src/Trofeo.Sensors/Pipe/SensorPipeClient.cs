using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Trofeo.Core.Rendering;

namespace Trofeo.Sensors;

/// <summary>Flux de relevés venus de l'assistant : le hub s'y abonne sans connaître le tube.</summary>
public interface ISnapshotStream
{
    /// <summary>Levé sur le thread de lecture du flux à chaque relevé valide reçu.</summary>
    event EventHandler<SensorSnapshot>? Received;
}

/// <summary>
/// Côté non élevé du tube : se connecte à l'assistant, vérifie qui sert le tube, puis relaie ses relevés.
/// Ne contacte jamais un serveur dont <c>isTrustedServer</c> ne valide pas le propriétaire, et ne lui envoie rien
/// (tube entrant). Réessaie toutes les 5 s tant qu'il n'est pas connecté.
/// </summary>
/// <param name="isTrustedServer">Reçoit le tube connecté ; faux = déconnexion (voir <see cref="HelperIdentity.IsTrusted"/>).</param>
public sealed class SensorPipeClient(string pipeName, Func<NamedPipeClientStream, bool> isTrustedServer, TimeProvider time) : ISnapshotStream, IDisposable
{
    private const int ConnectTimeoutMs = 1000;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stop = new();
    private readonly RateLimitedErrorLog _errors = new(time, m => Trace.TraceWarning(m));
    private volatile bool _connected;
    private bool _warnedUntrusted;

    /// <inheritdoc/>
    public event EventHandler<SensorSnapshot>? Received;

    /// <summary>Vrai tant qu'une connexion à un serveur de confiance est ouverte.</summary>
    public bool IsConnected => _connected;

    /// <summary>Boucle de connexion jusqu'à l'annulation (ou <see cref="Dispose"/>), qui termine la tâche normalement.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
        var token = linked.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReadAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or UnauthorizedAccessException)
            {
                // Pas d'assistant, assistant arrêté, ligne démesurée : cas normaux, on réessaie plus tard.
            }
            catch (Exception ex)
            {
                _errors.Report($"Lecture du tube des capteurs : {ex}");
            }

            try { await Task.Delay(RetryDelay, time, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Interrompt <see cref="RunAsync"/> et ferme la connexion en cours.</summary>
    public void Dispose() => _stop.Cancel();

    private async Task ConnectAndReadAsync(CancellationToken ct)
    {
        // Identification : même si le serveur obtenait une identité cliente, il ne pourrait pas agir en son nom.
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(ConnectTimeoutMs, ct);

        if (!isTrustedServer(pipe))
        {
            if (!_warnedUntrusted)
                Trace.TraceWarning($"Le tube des capteurs « {pipeName} » n'appartient ni à SYSTEM ni aux Administrateurs : relevés ignorés.");
            _warnedUntrusted = true;
            return;
        }

        _connected = true;
        try
        {
            var lines = new BoundedLineReader(pipe, SensorPipe.MaxLineBytes);
            while (await lines.ReadLineAsync(ct))
            {
                if (SensorPipe.TryParse(lines.Line.Span) is { } snapshot)
                    Received?.Invoke(this, snapshot);
            }
        }
        finally
        {
            _connected = false;
        }
    }
}
