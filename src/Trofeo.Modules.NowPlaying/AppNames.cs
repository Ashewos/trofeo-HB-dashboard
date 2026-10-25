namespace Trofeo.Modules.NowPlaying;

/// <summary>Nom lisible d'une application à partir de son identifiant de modèle utilisateur (AppUserModelId).</summary>
public static class AppNames
{
    private const string Fallback = "Média";

    // Fragments cherchés dans l'ordre, sans tenir compte de la casse.
    private static readonly (string Fragment, string Name)[] Known =
    [
        ("spotify", "Spotify"),
        ("msedge", "Edge"),
        ("chrome", "Chrome"),
        ("firefox", "Firefox"),
        ("zunemusic", "Lecteur multimédia"),
        ("vlc", "VLC"),
        ("deezer", "Deezer"),
        ("applemusic", "Apple Music"),
    ];

    /// <summary>
    /// Applications connues par leur nom d'usage ; sinon ce qui précède « ! », puis ce qui précède « _ », puis le dernier
    /// segment après « . » (une fois « .exe » retiré), avec une majuscule initiale. Vide ou <c>null</c> : « Média ».
    /// </summary>
    public static string Friendly(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId)) return Fallback;
        var id = appUserModelId.Trim();

        foreach (var (fragment, name) in Known)
        {
            if (id.Contains(fragment, StringComparison.OrdinalIgnoreCase)) return name;
        }

        var bang = id.IndexOf('!');
        if (bang >= 0) id = id[..bang];
        var underscore = id.IndexOf('_');
        if (underscore >= 0) id = id[..underscore];
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
        var dot = id.LastIndexOf('.');
        if (dot >= 0) id = id[(dot + 1)..];

        id = id.Trim();
        return id.Length == 0 ? Fallback : char.ToUpperInvariant(id[0]) + id[1..];
    }
}
