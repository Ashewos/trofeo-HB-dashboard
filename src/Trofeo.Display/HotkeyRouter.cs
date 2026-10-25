using System.Diagnostics;
using Trofeo.Core.Shortcuts;
using Trofeo.ModuleKit;

namespace Trofeo.Display;

/// <summary>Ce qui enregistre les raccourcis auprès de Windows : <see cref="HotkeyWindow"/> en production, un faux dans les tests.</summary>
public interface IHotkeyRegistrar
{
    /// <summary>Enregistre un raccourci global ; rend faux si Windows le refuse (déjà pris par une autre application).</summary>
    bool Register(int id, uint modifiers, uint virtualKey);

    /// <summary>Retire un raccourci enregistré par <see cref="Register"/>.</summary>
    void Unregister(int id);
}

/// <summary>
/// Enregistre les raccourcis de la disposition et envoie la commande de chacun au module visé. À utiliser depuis
/// le thread de l'interface seulement.
/// </summary>
/// <param name="resolveTarget">Trouve le module visé (<c>null</c> : le premier qui accepte les commandes).</param>
public sealed class HotkeyRouter(
    IHotkeyRegistrar registrar, IReadOnlyList<ShortcutBinding> bindings, Func<Guid?, ICommandableModule?> resolveTarget) : IDisposable
{
    /// <summary>Évite que le maintien de la touche n'envoie la commande en rafale.</summary>
    private const uint ModNoRepeat = 0x4000;

    private readonly HashSet<int> _registered = [];
    private bool _disposed;

    /// <summary>
    /// Enregistre les liaisons sous les identifiants 1 à N, dans l'ordre. Une touche illisible ou refusée par Windows
    /// est journalisée et laissée de côté : les autres sont enregistrées quand même.
    /// </summary>
    public void RegisterAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            var id = i + 1;
            if (!ShortcutKeys.TryParse(binding.Keys, out var modifiers, out var virtualKey))
            {
                Trace.TraceWarning($"Raccourci « {binding.Keys} » indisponible : touches illisibles.");
                continue;
            }

            if (!registrar.Register(id, modifiers | ModNoRepeat, virtualKey))
            {
                Trace.TraceWarning($"Raccourci « {binding.Keys} » indisponible : déjà pris par une autre application.");
                continue;
            }

            _registered.Add(id);
        }
    }

    /// <summary>Un raccourci a été pressé : envoie sa commande au module visé. Aucune exception n'en sort.</summary>
    public void OnHotkey(int id)
    {
        if (_disposed || id < 1 || id > bindings.Count) return;

        var binding = bindings[id - 1];
        try
        {
            resolveTarget(binding.Target)?.HandleCommand(new ModuleCommand(binding.Command, binding.Index));
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Échec du raccourci « {binding.Keys} » : {ex}");
        }
    }

    /// <summary>Retire tous les raccourcis enregistrés.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var id in _registered) registrar.Unregister(id);
        _registered.Clear();
    }
}
