using Trofeo.Core.Rendering;

namespace Trofeo.Settings.Services;

/// <summary>
/// Les fichiers que chaque sorte de fond sait lire. <c>BackgroundFactory</c> n'en tient pas de liste (il essaie le fichier
/// et avertit s'il ne marche pas) : celle-ci est faite d'après ses lecteurs, pour que le sélecteur ne propose que des
/// fichiers utiles.
/// </summary>
public static class BackgroundFiles
{
    /// <summary>Photo : <c>ImageBackground</c> (Skia : PNG, JPEG, WebP, BMP ; un GIF y serait lu image fixe, il a son choix).</summary>
    private static readonly string[] Photos = ["png", "jpg", "jpeg", "webp", "bmp"];

    /// <summary>GIF : <c>AnimatedBackground</c> (GIF ou WebP animé).</summary>
    private static readonly string[] Animations = ["gif", "webp"];

    /// <summary>Vidéo : Media Foundation (le lecteur de l'écran, sous Windows).</summary>
    private static readonly string[] Videos = ["mp4", "m4v", "mov", "wmv", "avi"];

    public static IReadOnlyList<string> Extensions(BackgroundKind kind) => kind switch
    {
        BackgroundKind.Image => Photos,
        BackgroundKind.Animated => Animations,
        BackgroundKind.Video => Videos,
        _ => [],
    };

    public static string Title(BackgroundKind kind) => kind switch
    {
        BackgroundKind.Image => Texts.AppearancePickPhoto,
        BackgroundKind.Animated => Texts.AppearancePickGif,
        _ => Texts.AppearancePickVideo,
    };

    public static string TypeName(BackgroundKind kind) => kind switch
    {
        BackgroundKind.Image => Texts.AppearanceTypePhotos,
        BackgroundKind.Animated => Texts.AppearanceTypeGifs,
        _ => Texts.AppearanceTypeVideos,
    };
}
