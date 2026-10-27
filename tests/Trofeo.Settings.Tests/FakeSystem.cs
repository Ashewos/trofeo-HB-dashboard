using Trofeo.Settings.Services;

namespace Trofeo.Settings.Tests;

/// <summary>Le démarrage avec Windows, sans registre : le test lit ce qui a été demandé.</summary>
public sealed class FakeStartupRegistration : IStartupRegistration
{
    public bool IsEnabled { get; set; }
    public string? EnabledWith { get; private set; }
    public int EnableCalls { get; private set; }
    public int DisableCalls { get; private set; }

    /// <summary>Windows refuse d'écrire dans la clé : le message de l'exception.</summary>
    public string? Failure { get; set; }

    public void Enable(string exePath)
    {
        EnableCalls++;
        if (Failure is not null)
            throw new UnauthorizedAccessException(Failure);
        EnabledWith = exePath;
        IsEnabled = true;
    }

    public void Disable()
    {
        DisableCalls++;
        if (Failure is not null)
            throw new UnauthorizedAccessException(Failure);
        IsEnabled = false;
    }
}

/// <summary>Le lancement élevé, sans fenêtre d'autorisation : le test dit si Windows « accepte » et lit ce qui a été lancé.</summary>
public sealed class FakeElevatedLauncher : IElevatedLauncher
{
    public bool Accept { get; set; } = true;
    public List<(string Exe, string Arguments)> Launched { get; } = [];

    public bool Launch(string exe, string arguments)
    {
        Launched.Add((exe, arguments));
        return Accept;
    }
}
