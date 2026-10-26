using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Trofeo.Settings.Services;

/// <summary>
/// Le choix d'un fichier par l'utilisateur. Derrière une interface pour que les tests donnent le chemin voulu sans
/// ouvrir de boîte de dialogue.
/// </summary>
public interface IFilePicker
{
    /// <summary>
    /// Ouvre le sélecteur. <paramref name="extensions"/> : sans point ni joker (<c>png</c>, <c>jpg</c>…). Rend le chemin
    /// complet du fichier choisi, ou <c>null</c> si l'utilisateur annule.
    /// </summary>
    Task<string?> PickAsync(string title, string typeName, IReadOnlyList<string> extensions);
}

/// <summary>Le sélecteur de fichier de la plate-forme (Avalonia <see cref="IStorageProvider"/>), ouvert sur une fenêtre.</summary>
public sealed class StorageFilePicker(TopLevel owner) : IFilePicker
{
    public async Task<string?> PickAsync(string title, string typeName, IReadOnlyList<string> extensions)
    {
        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = [.. extensions.Select(e => "*." + e)] }],
        };

        var files = await owner.StorageProvider.OpenFilePickerAsync(options).ConfigureAwait(true);
        return files.Count == 1 ? files[0].TryGetLocalPath() : null;
    }
}
