using System.Runtime.InteropServices;

namespace Trofeo.Display;

/// <summary>
/// Fenêtre invisible (message seul) qui reçoit <c>WM_HOTKEY</c> pour les raccourcis globaux. À créer sur le thread
/// de l'interface, avant <c>Application.Run</c> : c'est la pompe de messages de ce thread qui la sert.
/// </summary>
public sealed partial class HotkeyWindow : NativeWindow, IHotkeyRegistrar, IDisposable
{
    private const int WmHotkey = 0x0312;
    private static readonly nint HwndMessage = -3;

    public HotkeyWindow() => CreateHandle(new CreateParams { Parent = HwndMessage });

    /// <summary>Un raccourci enregistré a été pressé ; l'argument est son identifiant.</summary>
    public event Action<int>? Hotkey;

    public bool Register(int id, uint modifiers, uint virtualKey) => RegisterHotKey(Handle, id, modifiers, virtualKey);

    public void Unregister(int id) => UnregisterHotKey(Handle, id);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey) Hotkey?.Invoke((int)m.WParam);
        base.WndProc(ref m);
    }

    /// <summary>Détruit la fenêtre. Les raccourcis doivent déjà être retirés (<see cref="HotkeyRouter.Dispose"/>).</summary>
    public void Dispose()
    {
        Hotkey = null;
        DestroyHandle();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);
}
