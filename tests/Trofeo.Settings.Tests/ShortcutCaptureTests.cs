using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Trofeo.Control;
using Trofeo.Core.Layout;
using Trofeo.Core.Shortcuts;
using Trofeo.Settings.Services;
using Trofeo.Settings.ViewModels;
using Trofeo.Settings.Views;

namespace Trofeo.Settings.Tests;

/// <summary>Les touches d'Avalonia traduites en syntaxe de <see cref="ShortcutKeys"/>, et la capture dans la vraie fenêtre.</summary>
public sealed class ShortcutCaptureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "trofeo-settings-capture-" + Guid.NewGuid().ToString("N"));
    private readonly FakeServiceLink _link = new();
    private MainWindow? _window;

    public void Dispose()
    {
        _window?.Close();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData(Key.Right, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+Right")]
    [InlineData(Key.D3, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, "Ctrl+Alt+Shift+3")]
    [InlineData(Key.F5, KeyModifiers.Control, "Ctrl+F5")]
    [InlineData(Key.Left, KeyModifiers.Alt, "Alt+Left")]
    [InlineData(Key.Up, KeyModifiers.Control, "Ctrl+Up")]
    [InlineData(Key.Down, KeyModifiers.Control, "Ctrl+Down")]
    [InlineData(Key.A, KeyModifiers.Control | KeyModifiers.Meta, "Ctrl+Win+A")]
    [InlineData(Key.D0, KeyModifiers.Alt, "Alt+0")]
    [InlineData(Key.F24, KeyModifiers.Shift, "Shift+F24")]
    public void Capture_TranslatesToShortcutSyntax(Key key, KeyModifiers modifiers, string expected)
    {
        var keys = ShortcutCapture.Translate(key, modifiers);

        Assert.Equal(expected, keys);
        Assert.True(ShortcutKeys.TryParse(keys!, out _, out _)); // exactement ce que le service sait lire
    }

    [Fact]
    public void Capture_WithoutModifier_IsIgnored()
    {
        Assert.Null(ShortcutCapture.Translate(Key.Right, KeyModifiers.None));
        Assert.Null(ShortcutCapture.Translate(Key.F5, KeyModifiers.None));
        Assert.Null(ShortcutCapture.Translate(Key.A, KeyModifiers.None));
    }

    [Theory]
    [InlineData(Key.Space)]
    [InlineData(Key.Enter)]
    [InlineData(Key.Tab)]
    [InlineData(Key.NumPad1)]   // le code de touche du pavé numérique n'est pas celui du chiffre : ShortcutKeys ne le lit pas
    [InlineData(Key.OemPlus)]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LWin)]
    [InlineData(Key.None)]
    public void Capture_KeysShortcutKeysDoesNotRead_AreIgnored(Key key)
    {
        Assert.Null(ShortcutCapture.Translate(key, KeyModifiers.Control | KeyModifiers.Alt));
    }

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightCtrl)]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.RightShift)]
    [InlineData(Key.LWin)]
    [InlineData(Key.RWin)]
    public void IsModifier_RecognizesModifierKeys(Key key) => Assert.True(ShortcutCapture.IsModifier(key));

    [Fact]
    public void IsModifier_OtherKeys_AreNot() => Assert.False(ShortcutCapture.IsModifier(Key.A));

    [Theory]
    [InlineData("Ctrl+Alt+Right", "Ctrl + Alt + →")]
    [InlineData("Ctrl+Alt+Left", "Ctrl + Alt + ←")]
    [InlineData("ctrl+up", "Ctrl + ↑")]
    [InlineData("Alt+Down", "Alt + ↓")]
    [InlineData("Ctrl+Alt+Shift+3", "Ctrl + Alt + Maj + 3")]
    [InlineData("Ctrl+Win+f5", "Ctrl + Win + F5")]
    public void Display_WritesTheKeysAsPill(string keys, string expected) =>
        Assert.Equal(expected, ShortcutCapture.Display(keys));

    // ---- Dans la vraie fenêtre ----

    private (MainWindow Window, MainViewModel Model) Open()
    {
        Directory.CreateDirectory(_dir);
        _link.Set(LinkState.Connected, SampleData.Welcome(), SampleData.Status());
        var document = new LayoutDocument(new LayoutStore(Path.Combine(_dir, "layout.json")), _link);
        document.Load();
        var model = new MainViewModel(_link, document, new EditorPreferences());
        _window = new MainWindow { DataContext = model, Width = 1440, Height = 900 };
        _window.Show();
        model.SelectedTab = model.ShortcutsTab;
        Dispatcher.UIThread.RunJobs();
        return (_window, model);
    }

    [AvaloniaFact]
    public void ShortcutsTab_ShowsRowsAndButtons()
    {
        var (window, model) = Open();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("Onglet précédent", texts);
        Assert.Contains("Ctrl + Alt + →", texts);
        Assert.Contains("Aller à l'onglet 9", texts);
        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).ToList();
        Assert.Contains("Ajouter un raccourci", buttons);
        Assert.Contains("Remettre les raccourcis par défaut", buttons);
        Assert.Equal(11, buttons.Count(c => c == "Modifier"));
        Assert.Equal(11, buttons.Count(c => c == "Supprimer"));
        Assert.Equal(11, model.ShortcutsTab.Rows.Count);
    }

    [AvaloniaFact]
    public void ModifyButton_StartsCapture_AndTheKeyIsTaken()
    {
        var (window, model) = Open();
        var modify = window.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && Equals(b.Content, "Modifier"));

        modify.Command!.Execute(modify.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.True(model.ShortcutsTab.IsCapturing);
        Assert.Contains(Texts.ShortcutCapture,
            window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));

        window.KeyPress(Key.F7, RawInputModifiers.Control, PhysicalKey.F7, null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(model.ShortcutsTab.IsCapturing);
        Assert.Equal("Ctrl+F7", model.Document.Current.Shortcuts![0].Keys);
    }

    [AvaloniaFact]
    public async Task Capture_CtrlZ_IsCaptured_NotTakenForUndo()
    {
        var (window, model) = Open();
        await model.ShortcutsTab.DeleteAsync(model.ShortcutsTab.Rows[0]);   // une modification à annuler
        Assert.Equal(10, model.ShortcutsTab.Rows.Count);

        model.ShortcutsTab.BeginCapture(model.ShortcutsTab.Rows[0]);
        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
        Dispatcher.UIThread.RunJobs();

        // Ctrl+Z est la combinaison voulue, pas une annulation : la suppression reste, et l'historique n'a pas reculé.
        Assert.Equal(10, model.ShortcutsTab.Rows.Count);
        Assert.Equal("Ctrl+Z", model.Document.Current.Shortcuts![0].Keys);
        Assert.False(model.Session.CanRedo);
    }

    [AvaloniaFact]
    public void Capture_Escape_CancelsWithoutLeavingTheTab()
    {
        var (window, model) = Open();
        model.ShortcutsTab.BeginCapture(model.ShortcutsTab.Rows[2]);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.False(model.ShortcutsTab.IsCapturing);
        Assert.Same(model.ShortcutsTab, model.SelectedTab);
        Assert.Equal(0, _link.ReloadCount);
    }

    [AvaloniaFact]
    public void Capture_LeavingTheTab_Cancels()
    {
        var (_, model) = Open();
        model.ShortcutsTab.BeginCapture(model.ShortcutsTab.Rows[2]);

        model.SelectedTab = model.SettingsTab;

        Assert.False(model.ShortcutsTab.IsCapturing);
    }

    [AvaloniaFact]
    public void NotCapturing_CtrlZ_StillUndoes()
    {
        var (window, model) = Open();
        _ = model.ShortcutsTab.DeleteAsync(model.ShortcutsTab.Rows[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(10, model.ShortcutsTab.Rows.Count);

        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(11, model.ShortcutsTab.Rows.Count);
    }

    [AvaloniaFact]
    public async Task HotkeyFailure_IsOnItsRow_NotRepeatedInTheBanner_OnTheShortcutsTab()
    {
        const string failure = "Raccourci « Ctrl+Alt+Right » indisponible : déjà pris par une autre application.";
        const string other = "Le module Heure n'a pas pu démarrer.";
        _link.OnReload = () => Task.FromResult(new Reloaded(true, null, [failure, other]));
        var (_, model) = Open();

        await model.ShortcutsTab.DeleteAsync(model.ShortcutsTab.Rows[0]);

        Assert.Equal(failure, model.ShortcutsTab.Rows[0].Failure);
        Assert.Equal(other, model.SaveMessage);                    // le reste du rechargement reste sous la bande

        model.SelectedTab = model.SettingsTab;                      // ailleurs, personne ne l'a sous les yeux : la bande le dit
        Assert.Equal(failure + "\n" + other, model.SaveMessage);

        model.SelectedTab = model.ShortcutsTab;
        Assert.Equal(other, model.SaveMessage);
    }
}
