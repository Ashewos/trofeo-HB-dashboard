using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Trofeo.Core.Rendering;

namespace Trofeo.Sensors;

/// <summary>
/// Côté élevé du tube : publie un relevé par seconde à l'unique client connecté. Le tube est sortant
/// (<see cref="PipeDirection.Out"/>) et ce serveur ne lit jamais rien : un processus non privilégié ne peut donc
/// rien lui faire traiter. Les droits d'accès sont SYSTEM et Administrateurs en contrôle total, utilisateurs
/// authentifiés en lecture seule, accès réseau refusé. Le client reconnaît ce serveur à son propriétaire
/// (voir <see cref="HelperIdentity"/>), qu'un utilisateur ordinaire ne peut pas se donner.
/// </summary>
public sealed class SensorPipeServer(string pipeName, ISensorReader reader, TimeProvider time)
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    // Un client qui se connecte sans jamais lire remplirait le tampon et bloquerait l'unique instance : on le coupe.
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    private readonly RateLimitedErrorLog _errors = new(time, m => Trace.TraceWarning(m));

    /// <summary>
    /// Sert les clients l'un après l'autre jusqu'à l'annulation, qui termine la tâche normalement. Un client parti
    /// ramène à l'attente du suivant sur un tube recréé ; si le nom est déjà pris (autre processus, autre
    /// assistant), la création est retentée toutes les 5 s sans bruit supplémentaire dans le journal.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var warned = false;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = Create();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!warned) Trace.TraceWarning($"Tube des capteurs « {pipeName} » indisponible : {ex.Message}");
                warned = true;
                try { await Task.Delay(RetryDelay, time, ct); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            warned = false;
            await using (pipe)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(ct);
                    await ServeAsync(pipe, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                {
                    // Client parti (ou bloqué) : l'instance est libérée avant d'en recréer une.
                }
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        while (true)
        {
            byte[]? line = null;
            try
            {
                line = SensorPipe.Serialize(reader.Read());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Une erreur passagère du lecteur ne doit pas arrêter l'assistant : on saute ce relevé et on garde la connexion.
                _errors.Report($"Lecture des capteurs impossible : {ex}");
            }

            if (line is not null)
                await pipe.WriteAsync(line, ct).AsTask().WaitAsync(WriteTimeout, time, ct);
            await Task.Delay(Period, time, ct);
        }
    }

    private NamedPipeServerStream Create() =>
        NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, BuildSecurity());

    internal static PipeSecurity BuildSecurity()
    {
        var security = new PipeSecurity();
        // Le refus réseau passe en premier : aucun client distant, quels que soient ses autres droits.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        // Synchronize : sans lui, l'ouverture asynchrone par le client est refusée. Read inclut la lecture du
        // descripteur de sécurité, dont le client a besoin pour vérifier le propriétaire du tube.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.Read | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return security;
    }
}
