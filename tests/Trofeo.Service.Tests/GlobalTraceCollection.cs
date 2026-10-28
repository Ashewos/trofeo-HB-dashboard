namespace Trofeo.Display.Tests;

/// <summary>
/// Les tests qui écoutent le journal global (<c>Trace.Listeners</c>) tournent seuls : sinon ils lisent aussi les lignes des
/// boucles des tests voisins (« Image 1 envoyée … rendu 0 ms » d'un autre test, par exemple).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalTraceCollection
{
    public const string Name = "Journal global";
}
