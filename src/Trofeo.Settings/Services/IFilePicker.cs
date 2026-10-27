using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Trofeo.Settings.Services;

/// <summary>
/// Le choix d'un fichier par l'utilisateur. Derrière une interface pour que les tests donnent le chemin voulu sans
/// ouvrir de boîte de dialogue. Un sélecteur qui ne s'ouvre pas lève <see cref="FilePickerException"/>.
/// </summary>
public interface IFilePicker
{
    /// <summary>
    /// Ouvre le sélecteur. <paramref name="extensions"/> : sans point ni joker (<c>png</c>, <c>jpg</c>…). Rend le chemin
    /// complet du fichier choisi, ou <c>null</c> si l'utilisateur annule.
    /// </summary>
    Task<string?> PickAsync(string title, string typeName, IReadOnlyList<string> extensions);

    /// <summary>
    /// Ouvre « Enregistrer sous ». <paramref name="extension"/> : sans point ni joker (<c>trofeo.json</c>) ;
    /// <paramref name="suggestedName"/> : le nom proposé, extension comprise. Rend le chemin complet choisi, ou <c>null</c>
    /// si l'utilisateur annule.
    /// </summary>
    Task<string?> SaveAsync(string title, string typeName, string extension, string suggestedName);
}

/// <summary>Le sélecteur n'a pas pu s'ouvrir ; le message est en français, prêt à être montré.</summary>
public sealed class FilePickerException(string message, Exception? inner = null) : Exception(message, inner);

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

        try
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(options).ConfigureAwait(true);
            if (files.Count != 1)
                return null;
            using var file = files[0];
            return file.TryGetLocalPath();
        }
        catch (OperationCanceledException)
        {
            return null; // fenêtre fermée pendant le choix : comme une annulation
        }
        catch (Exception ex)
        {
            throw new FilePickerException(string.Format(Texts.AppearancePickFailed, ex.Message), ex);
        }
    }

    public async Task<string?> SaveAsync(string title, string typeName, string extension, string suggestedName)
    {
        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = ["*." + extension] }],
        };

        try
        {
            using var file = await owner.StorageProvider.SaveFilePickerAsync(options).ConfigureAwait(true);
            return file?.TryGetLocalPath();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw new FilePickerException(string.Format(Texts.AppearancePickFailed, ex.Message), ex);
        }
    }
}
