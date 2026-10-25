using System.Diagnostics;
using Trofeo.Core.Shortcuts;
using Trofeo.ModuleKit;

namespace Trofeo.Display.Tests;

/// <summary>Les écouteurs Trace sont globaux au processus : ces tests ne tournent pas en parallèle avec ceux qui les lisent.</summary>
[Collection("Trace")]
public sealed class HotkeyRouterTests
{
    private const uint NoRepeat = 0x4000;

    private sealed class FakeRegistrar : IHotkeyRegistrar
    {
        public List<(int Id, uint Modifiers, uint VirtualKey)> Registered { get; } = [];
        public List<int> Unregistered { get; } = [];
        public HashSet<int> Refused { get; } = [];

        public bool Register(int id, uint modifiers, uint virtualKey)
        {
            if (Refused.Contains(id)) return false;
            Registered.Add((id, modifiers, virtualKey));
            return true;
        }

        public void Unregister(int id) => Unregistered.Add(id);
    }

    private sealed class FakeTarget(bool throws = false) : ICommandableModule
    {
        public List<ModuleCommand> Commands { get; } = [];

        public bool HandleCommand(ModuleCommand command)
        {
            Commands.Add(command);
            if (throws) throw new InvalidOperationException("commande refusée");
            return true;
        }
    }

    private static readonly ShortcutBinding Next = new("Ctrl+Alt+Right", ModuleCommandKind.NextTab);
    private static readonly ShortcutBinding Third = new("Ctrl+Shift+F5", ModuleCommandKind.SelectTab, 2);

    private static string[] Capture(Action action)
    {
        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            action();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        return listener.Lines;
    }

    [Fact]
    public void Router_RegistersAllWithNoRepeat()
    {
        var registrar = new FakeRegistrar();
        using var router = new HotkeyRouter(registrar, [Next, Third], _ => null);

        router.RegisterAll();

        Assert.Equal([(1, 3u | NoRepeat, 0x27u), (2, 6u | NoRepeat, 0x74u)], registrar.Registered);
    }

    [Fact]
    public void Router_RegisterFails_ContinuesWithOthers()
    {
        var registrar = new FakeRegistrar { Refused = { 1 } };
        using var router = new HotkeyRouter(registrar, [Next, Third], _ => null);

        var lines = Capture(router.RegisterAll);

        Assert.Equal([2], registrar.Registered.Select(r => r.Id));
        Assert.Contains(lines, l => l.Contains("Raccourci « Ctrl+Alt+Right » indisponible"));
    }

    [Fact]
    public void Router_InvalidKeys_Skipped()
    {
        var registrar = new FakeRegistrar();
        using var router = new HotkeyRouter(registrar, [new ShortcutBinding("Foo", ModuleCommandKind.NextTab), Third], _ => null);

        var lines = Capture(router.RegisterAll);

        Assert.Equal([2], registrar.Registered.Select(r => r.Id));
        Assert.Contains(lines, l => l.Contains("Raccourci « Foo » indisponible"));
    }

    [Fact]
    public void Router_OnHotkey_SendsCommandToTarget()
    {
        var target = new FakeTarget();
        var instance = Guid.NewGuid();
        var asked = new List<Guid?>();
        using var router = new HotkeyRouter(
            new FakeRegistrar(), [Next, Third with { Target = instance }], id =>
            {
                asked.Add(id);
                return target;
            });
        router.RegisterAll();

        router.OnHotkey(1);
        router.OnHotkey(2);
        router.OnHotkey(99); // identifiant inconnu : ignoré

        Assert.Equal([(Guid?)null, instance], asked);
        Assert.Equal(
            [new ModuleCommand(ModuleCommandKind.NextTab), new ModuleCommand(ModuleCommandKind.SelectTab, 2)],
            target.Commands);
    }

    [Fact]
    public void Router_OnHotkey_NoTarget_Ignored()
    {
        using var router = new HotkeyRouter(new FakeRegistrar(), [Next], _ => null);
        router.RegisterAll();

        router.OnHotkey(1); // ne lève rien
    }

    [Fact]
    public void Router_HandlerThrows_Logged()
    {
        var target = new FakeTarget(throws: true);
        using var router = new HotkeyRouter(new FakeRegistrar(), [Next], _ => target);
        router.RegisterAll();

        var lines = Capture(() => router.OnHotkey(1));

        Assert.Single(target.Commands);
        Assert.Contains(lines, l => l.Contains("Ctrl+Alt+Right") && l.Contains("commande refusée"));
    }

    [Fact]
    public void Router_Dispose_UnregistersAll()
    {
        var registrar = new FakeRegistrar { Refused = { 2 } };
        var router = new HotkeyRouter(registrar, [Next, Third, new ShortcutBinding("Foo", ModuleCommandKind.NextTab)], _ => null);
        router.RegisterAll();

        router.Dispose();
        router.Dispose(); // idempotent

        Assert.Equal([1], registrar.Unregistered); // seulement ce qui a été enregistré
    }

    [Fact]
    public void Router_OnHotkeyAfterDispose_Ignored()
    {
        var target = new FakeTarget();
        var router = new HotkeyRouter(new FakeRegistrar(), [Next], _ => target);
        router.RegisterAll();
        router.Dispose();

        router.OnHotkey(1);

        Assert.Empty(target.Commands);
    }
}
