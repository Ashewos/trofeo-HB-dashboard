using Trofeo.Settings.Services;

namespace Trofeo.Settings.Tests;

/// <summary>Un sélecteur de fichier que le test pilote : il rend le chemin voulu (ou <c>null</c> : l'utilisateur annule) et note ce qu'on lui a demandé.</summary>
public sealed class FakeFilePicker : IFilePicker
{
    public string? Result { get; set; }
    public int Calls { get; private set; }
    public string? Title { get; private set; }
    public IReadOnlyList<string> Extensions { get; private set; } = [];

    public Task<string?> PickAsync(string title, string typeName, IReadOnlyList<string> extensions)
    {
        Calls++;
        Title = title;
        Extensions = extensions;
        return Task.FromResult(Result);
    }
}
