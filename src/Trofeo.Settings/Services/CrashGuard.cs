using System.Diagnostics;
using Avalonia.Threading;

namespace Trofeo.Settings.Services;

/// <summary>
/// Le filet de la fenêtre : une exception que personne n'attrape est écrite au journal au lieu de fermer la fenêtre sans
/// trace. Sur le fil de l'interface (une commande asynchrone qui lève, par exemple), elle est traitée : la fenêtre reste
/// ouverte et la ligne ambre dit « Erreur inattendue : … ». Ailleurs (<see cref="AppDomain.UnhandledException"/>,
/// <see cref="TaskScheduler.UnobservedTaskException"/>), elle est journalisée, et une tâche non observée est observée.
/// </summary>
internal sealed class CrashGuard : IDisposable
{
    private readonly Action<string> _log;
    private UnhandledExceptionEventHandler? _domainHandler;
    private EventHandler<UnobservedTaskExceptionEventArgs>? _taskHandler;
    private DispatcherUnhandledExceptionEventHandler? _uiHandler;

    /// <param name="log">Reçoit une ligne par erreur (<see cref="Trace.TraceError(string)"/> par défaut).</param>
    public CrashGuard(Action<string>? log = null) => _log = log ?? (message => Trace.TraceError(message));

    /// <summary>Montre un message sur la ligne ambre de la fenêtre ; posé quand la fenêtre existe.</summary>
    public Action<string>? Show { get; set; }

    /// <summary>
    /// Une exception non gérée du fil de l'interface : journalisée, montrée sur la ligne ambre. Rend vrai (traitée : la
    /// fenêtre reste ouverte).
    /// </summary>
    public bool OnUiThread(Exception exception)
    {
        _log($"Exception de l'interface : {exception}");
        try
        {
            Show?.Invoke(string.Format(Texts.UnexpectedError, exception.Message));
        }
        catch (Exception ex)
        {
            _log($"La ligne d'erreur n'a pas pu s'afficher : {ex.Message}");
        }

        return true;
    }

    /// <summary>Une exception non gérée d'un autre fil : journalisée (le processus s'arrête si <paramref name="terminating"/>).</summary>
    public void OnDomain(object? exceptionObject, bool terminating)
    {
        _log($"Exception non gérée{(terminating ? " (arrêt de la fenêtre)" : "")} : {exceptionObject}");
        Trace.Flush();
    }

    /// <summary>Une tâche a levé sans que personne l'attende : journalisée, observée (elle ne ferme rien).</summary>
    public void OnUnobservedTask(UnobservedTaskExceptionEventArgs e)
    {
        _log($"Tâche en échec, non attendue : {e.Exception}");
        e.SetObserved();
    }

    /// <summary>Branche les gestionnaires qui valent pour tout le processus (domaine, tâches) : à poser avant l'interface.</summary>
    public void InstallProcessHandlers()
    {
        if (_domainHandler is not null)
            return;

        _domainHandler = (_, e) => OnDomain(e.ExceptionObject, e.IsTerminating);
        _taskHandler = (_, e) => OnUnobservedTask(e);
        AppDomain.CurrentDomain.UnhandledException += _domainHandler;
        TaskScheduler.UnobservedTaskException += _taskHandler;
    }

    /// <summary>Branche le gestionnaire du fil de l'interface : à poser une fois l'interface démarrée, sur son fil.</summary>
    public void InstallUiHandler()
    {
        if (_uiHandler is not null)
            return;

        _uiHandler = (_, e) => e.Handled = OnUiThread(e.Exception);
        Dispatcher.UIThread.UnhandledException += _uiHandler;
    }

    public void Dispose()
    {
        if (_uiHandler is not null)
            Dispatcher.UIThread.UnhandledException -= _uiHandler;
        if (_domainHandler is not null)
            AppDomain.CurrentDomain.UnhandledException -= _domainHandler;
        if (_taskHandler is not null)
            TaskScheduler.UnobservedTaskException -= _taskHandler;
        _uiHandler = null;
        _domainHandler = null;
        _taskHandler = null;
    }
}
