using SkiaSharp;
using Trofeo.Core.Layout;

namespace Trofeo.Core.Rendering;

/// <summary>
/// Le fond de la maquette (<c>.td-wall</c>) : dégradé linéaire à 120° (#142A3A, #241A3D à 55 %, #0F2D33) et quatre halos
/// elliptiques. Calculé une seule fois, à la construction.
/// </summary>
public sealed class AuroraBackground : IBackground
{
    /// <summary>
    /// Halo CSS <c>radial-gradient(ellipse RX% RY% at CX% CY%, couleur 0%, transparent STOP%)</c> : rayons en % de la
    /// largeur et de la hauteur, couleur pleine au centre, transparente (même teinte, alpha 0) à STOP du rayon.
    /// </summary>
    private readonly record struct Halo(string Color, float RadiusX, float RadiusY, float CenterX, float CenterY, float Stop);

    // Dans l'ordre du CSS : le premier est dessiné en dernier, donc au-dessus.
    private static readonly Halo[] Halos =
    [
        new("#FF7A59", .40f, .90f, .12f, .85f, .60f),
        new("#1FB8A6", .35f, .80f, .48f, .10f, .65f),
        new("#6D5DFC", .30f, .70f, .82f, .90f, .60f),
        new("#FFCF5A", .25f, .60f, .95f, .15f, .60f),
    ];

    private readonly SKImage _image;

    public AuroraBackground()
    {
        const float w = GridGeometry.Width, h = GridGeometry.Height;
        using var bitmap = new SKBitmap(BackgroundSupport.ScreenInfo);
        using (var canvas = new SKCanvas(bitmap))
        {
            // CSS 120deg : angle compté depuis le haut, dans le sens horaire (direction vers le bas à droite). La ligne
            // du dégradé passe par le centre de la boîte et mesure |l·sin| + |h·cos|, comme en CSS.
            const double angle = 120 * Math.PI / 180;
            float dx = (float)Math.Sin(angle), dy = (float)-Math.Cos(angle);
            float half = (Math.Abs(w * dx) + Math.Abs(h * dy)) / 2f;
            using (var linear = new SKPaint())
            {
                linear.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(w / 2 - dx * half, h / 2 - dy * half),
                    new SKPoint(w / 2 + dx * half, h / 2 + dy * half),
                    [SKColor.Parse("#142A3A"), SKColor.Parse("#241A3D"), SKColor.Parse("#0F2D33")],
                    [0f, .55f, 1f],
                    SKShaderTileMode.Clamp);
                canvas.DrawRect(0, 0, w, h, linear);
            }

            // Du dernier au premier : le premier du CSS finit au-dessus.
            for (int i = Halos.Length - 1; i >= 0; i--)
            {
                var halo = Halos[i];
                var color = SKColor.Parse(halo.Color);
                // Cercle de rayon 1 étiré en ellipse ; « transparent » = la même teinte à alpha 0 (sans frange sombre).
                var matrix = SKMatrix.CreateScaleTranslation(halo.RadiusX * w, halo.RadiusY * h, halo.CenterX * w, halo.CenterY * h);
                using var paint = new SKPaint { IsAntialias = false };
                paint.Shader = SKShader.CreateRadialGradient(
                    SKPoint.Empty, 1f, [color, color.WithAlpha(0)], [0f, halo.Stop], SKShaderTileMode.Clamp, matrix);
                canvas.DrawRect(0, 0, w, h, paint);
            }
        }
        _image = BackgroundSupport.Freeze(bitmap);
    }

    public bool IsAnimated => false;
    public SKImage Current => _image;
    public int Version => 1;
    public bool Advance(TimeSpan elapsed) => false;
    public TimeSpan? UntilNextFrame => null;

    public void Dispose() => _image.Dispose();
}
