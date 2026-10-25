using Trofeo.Core.Shortcuts;
using Trofeo.ModuleKit;

namespace Trofeo.Core.Tests;

public sealed class ShortcutKeysTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Right", 3u, 0x27u)]
    [InlineData("ctrl+shift+F5", 6u, 0x74u)]
    [InlineData("Alt+1", 1u, 0x31u)]
    [InlineData("Win+Shift+a", 12u, 0x41u)]
    [InlineData("Ctrl+F24", 2u, 0x87u)]
    [InlineData("Ctrl+Alt+Left", 3u, 0x25u)]
    [InlineData("Ctrl+Up", 2u, 0x26u)]
    [InlineData("Ctrl+Down", 2u, 0x28u)]
    public void TryParse_Table(string keys, uint modifiers, uint virtualKey)
    {
        Assert.True(ShortcutKeys.TryParse(keys, out var mods, out var vk));
        Assert.Equal((modifiers, virtualKey), (mods, vk));
    }

    [Theory]
    [InlineData("Right")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt+Foo")]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("A+Ctrl")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+A+B")]
    public void TryParse_Invalid_ReturnsFalse(string keys)
    {
        Assert.False(ShortcutKeys.TryParse(keys, out var mods, out var vk));
        Assert.Equal((0u, 0u), (mods, vk));
    }

    [Fact]
    public void Defaults_ElevenBindings()
    {
        var all = ShortcutDefaults.All;

        Assert.Equal(11, all.Count);
        Assert.Equal(new ShortcutBinding("Ctrl+Alt+Left", ModuleCommandKind.PreviousTab), all[0]);
        Assert.Equal(new ShortcutBinding("Ctrl+Alt+Right", ModuleCommandKind.NextTab), all[1]);
        for (var n = 1; n <= 9; n++)
            Assert.Equal(new ShortcutBinding($"Ctrl+Alt+{n}", ModuleCommandKind.SelectTab, n - 1), all[n + 1]);
        Assert.All(all, b => Assert.Null(b.Target));
        Assert.All(all, b => Assert.True(ShortcutKeys.TryParse(b.Keys, out _, out _)));
    }
}
