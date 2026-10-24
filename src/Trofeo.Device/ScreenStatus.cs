namespace Trofeo.Device;

/// <summary>L'état de l'écran et son motif, lus d'un seul bloc : jamais un état avec le motif d'un autre.</summary>
/// <param name="State">L'état courant.</param>
/// <param name="Reason">Motif en français quand l'état est <see cref="ScreenState.Disconnected"/>, sinon <c>null</c>.</param>
public sealed record ScreenStatus(ScreenState State, string? Reason);
