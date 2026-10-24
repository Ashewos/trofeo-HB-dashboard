namespace Trofeo.ModuleKit;

public enum ModuleHealth { NotConfigured, Online, Offline }

/// <summary>État d'un module, avec un éventuel message d'action pour l'utilisateur.</summary>
public sealed record ModuleStatus(ModuleHealth Health, string? ActionMessage = null);
