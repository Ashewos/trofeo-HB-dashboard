using System.Runtime.InteropServices;

namespace Trofeo.Settings.Services;

/// <summary>
/// Une seule fenêtre de réglage à la fois (l'icône de notification relance l'exécutable à chaque clic). Le premier
/// lancement prend un mutex nommé ; un second lancement le constate, prévient le premier par un <see cref="EventWaitHandle"/>
/// nommé (Windows seulement : ailleurs, ces objets nommés n'existent pas), puis doit quitter.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\trofeo-HB-dashboard.settings";
    public const string ShowEventName = @"Local\trofeo-HB-dashboard.settings.show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _show;
    private readonly ManualResetEvent? _stop;
    private readonly Thread? _waiter;

    private SingleInstance(Mutex mutex, bool isFirst, EventWaitHandle? show)
    {
        _mutex = mutex;
        IsFirst = isFirst;
        _show = show;
        if (isFirst && show is not null)
        {
            _stop = new ManualResetEvent(false);
            _waiter = new Thread(WaitForSecondLaunch) { IsBackground = true, Name = "Réglages : second lancement" };
            _waiter.Start();
        }
    }

    /// <summary>Vrai pour le premier lancement. Un second lancement a déjà prévenu le premier et doit quitter.</summary>
    public bool IsFirst { get; }

    /// <summary>Un second lancement demande de ramener la fenêtre au premier plan. Levé sur un fil d'arrière-plan.</summary>
    public event Action? ShowRequested;

    public static SingleInstance Acquire(string mutexName = MutexName, string showEventName = ShowEventName)
    {
        // Pas de propriété du mutex (premier paramètre faux) : il n'y a rien à libérer, et la sortie du processus suffit.
        var mutex = new Mutex(false, mutexName, out var createdNew);
        var show = OperatingSystem.IsWindows() ? new EventWaitHandle(false, EventResetMode.AutoReset, showEventName) : null;
        if (!createdNew)
        {
            // Le second lancement vient d'être cliqué : il a le droit de donner le premier plan à la fenêtre déjà ouverte.
            AllowForeground();
            show?.Set();
        }

        return new SingleInstance(mutex, createdNew, show);
    }

    private void WaitForSecondLaunch()
    {
        try
        {
            while (WaitHandle.WaitAny([_show!, _stop!]) == 0)
            {
                try { ShowRequested?.Invoke(); }
                catch { /* un abonné défaillant n'arrête pas l'écoute */ }
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        _stop?.Set();
        _waiter?.Join(TimeSpan.FromSeconds(1));
        _stop?.Dispose();
        _show?.Dispose();
        _mutex.Dispose();
    }

    private static void AllowForeground()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try { AllowSetForegroundWindow(-1); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
