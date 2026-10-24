using System.Diagnostics;
using Trofeo.Core.Rendering;
using Trofeo.ModuleKit;

namespace Trofeo.Core.Logging;

/// <summary>
/// Journal d'une instance de module, écrit via <see cref="Trace"/>. Chaque message est préfixé de
/// <c>[type instance]</c> (les 8 premiers caractères de l'identifiant d'instance). Les erreurs identiques
/// ne sont écrites en entier qu'une fois par minute (<see cref="RateLimitedErrorLog"/>).
/// </summary>
public sealed class TraceModuleLog : IModuleLog
{
    private readonly string _prefix;
    private readonly RateLimitedErrorLog _errors;

    public TraceModuleLog(string typeId, Guid instanceId, TimeProvider time)
    {
        _prefix = $"[{typeId} {instanceId.ToString("N")[..8]}] ";
        _errors = new RateLimitedErrorLog(time, m => Trace.TraceError(m));
    }

    public void Info(string message) => Trace.TraceInformation(_prefix + message);

    public void Warning(string message) => Trace.TraceWarning(_prefix + message);

    public void Error(string message) => _errors.Report(_prefix + message);
}
