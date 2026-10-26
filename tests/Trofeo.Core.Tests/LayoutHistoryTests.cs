using Trofeo.Core.Layout;

namespace Trofeo.Core.Tests;

public class LayoutHistoryTests
{
    private static DashboardLayout Layout(int tag) => new(180, [], MaxFps: tag);

    [Fact]
    public void History_UndoRedo_AndCapacity()
    {
        var history = new LayoutHistory();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.Undo(Layout(0)));
        Assert.Null(history.Redo(Layout(0)));

        // 51 Push : le plus ancien (1) est perdu, il en reste 50 (2 à 51).
        for (var i = 1; i <= 51; i++) history.Push(Layout(i));

        var current = Layout(100);
        var undone = 0;
        DashboardLayout? last = null;
        while (history.Undo(current) is { } before)
        {
            last = before;
            current = before;
            undone++;
        }

        Assert.Equal(50, undone);
        Assert.Equal(2, last!.MaxFps);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        // Refaire remonte jusqu'à l'état de départ.
        var redone = 0;
        while (history.Redo(current) is { } after)
        {
            current = after;
            redone++;
        }

        Assert.Equal(50, redone);
        Assert.Equal(100, current.MaxFps);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Undo_ReturnsPushedLayout_AndRedoReturnsTheCurrentOne()
    {
        var history = new LayoutHistory();
        var before = Layout(1);
        var after = Layout(2);
        history.Push(before);

        Assert.True(history.CanUndo);
        Assert.Same(before, history.Undo(after));
        Assert.True(history.CanRedo);
        Assert.Same(after, history.Redo(before));
        Assert.False(history.CanRedo);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void Push_AfterUndo_ClearsRedo()
    {
        var history = new LayoutHistory();
        history.Push(Layout(1));
        history.Undo(Layout(2));
        Assert.True(history.CanRedo);

        history.Push(Layout(3));

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Capacity_MustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LayoutHistory(0));
    }
}
