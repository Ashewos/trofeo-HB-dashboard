using System.Diagnostics;

namespace Trofeo.TestDoubles;

/// <summary>Écouteur de trace qui garde les lignes écrites, pour les vérifier dans les tests.</summary>
public sealed class CapturingTraceListener : TraceListener
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];

    public string[] Lines
    {
        get { lock (_gate) return [.. _lines]; }
    }

    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message)
    {
        lock (_gate) _lines.Add(message ?? "");
    }
}
