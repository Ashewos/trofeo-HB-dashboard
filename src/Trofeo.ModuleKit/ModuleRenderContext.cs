using SkiaSharp;

namespace Trofeo.ModuleKit;

/// <summary>Contexte de dessin d'un module. Les tailles de police sont à multiplier par <see cref="TextScale"/>.</summary>
public sealed record ModuleRenderContext(
    SKRect Bounds,
    GridSize Size,
    Theme Theme,
    float TextScale,
    DateTimeOffset Now);
