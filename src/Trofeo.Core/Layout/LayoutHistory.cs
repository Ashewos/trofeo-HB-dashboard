namespace Trofeo.Core.Layout;

/// <summary>Historique d'annulation et de rétablissement des dispositions, borné à <c>capacity</c> étapes.</summary>
/// <param name="capacity">Nombre d'étapes gardées (50 par défaut) ; la plus ancienne est perdue au-delà.</param>
public sealed class LayoutHistory(int capacity = 50)
{
    private readonly int _capacity = capacity >= 1
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "La capacité doit être d'au moins 1.");

    private readonly List<DashboardLayout> _undo = [];
    private readonly List<DashboardLayout> _redo = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Garde la disposition d'avant une modification ; une nouvelle modification efface ce qu'on pouvait rétablir.</summary>
    public void Push(DashboardLayout before)
    {
        _redo.Clear();
        Add(_undo, before);
    }

    /// <summary>La disposition d'avant la dernière modification (<paramref name="current"/> devient rétablissable), ou <c>null</c> s'il n'y a rien à annuler.</summary>
    public DashboardLayout? Undo(DashboardLayout current)
    {
        if (_undo.Count == 0) return null;

        var before = Pop(_undo);
        Add(_redo, current);
        return before;
    }

    /// <summary>La disposition d'avant la dernière annulation (<paramref name="current"/> redevient annulable), ou <c>null</c>.</summary>
    public DashboardLayout? Redo(DashboardLayout current)
    {
        if (_redo.Count == 0) return null;

        var after = Pop(_redo);
        Add(_undo, current);
        return after;
    }

    private void Add(List<DashboardLayout> stack, DashboardLayout layout)
    {
        stack.Add(layout);
        if (stack.Count > _capacity) stack.RemoveAt(0);
    }

    private static DashboardLayout Pop(List<DashboardLayout> stack)
    {
        var last = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return last;
    }
}
