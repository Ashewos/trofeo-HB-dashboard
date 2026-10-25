using System.Text.Json;

namespace Trofeo.Modules.Network;

/// <summary>Origine du débit affiché.</summary>
public enum NetworkSourceKind { Pc, Freebox, UniFi }

/// <summary>
/// Réglages du module Réseau, lus de <c>{"source":"pc"|"freebox"|"unifi","unifi":{"host":"192.168.1.1"}}</c>.
/// </summary>
public sealed record NetworkSettings(NetworkSourceKind Source = NetworkSourceKind.Pc, string? UniFiHost = null)
{
    /// <summary>
    /// Lit les réglages sans jamais lever : une source absente ou inconnue donne le PC (sans tenir compte de la
    /// casse), un hôte UniFi absent ou vide donne <c>null</c>.
    /// </summary>
    public static NetworkSettings FromJson(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object) return new NetworkSettings();

        var source = NetworkSourceKind.Pc;
        if (settings.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String)
        {
            source = s.GetString()?.Trim().ToLowerInvariant() switch
            {
                "freebox" => NetworkSourceKind.Freebox,
                "unifi" => NetworkSourceKind.UniFi,
                _ => NetworkSourceKind.Pc,
            };
        }

        string? host = null;
        if (settings.TryGetProperty("unifi", out var u) && u.ValueKind == JsonValueKind.Object
            && u.TryGetProperty("host", out var h) && h.ValueKind == JsonValueKind.String)
        {
            var text = h.GetString()?.Trim();
            host = string.IsNullOrEmpty(text) ? null : text;
        }

        return new NetworkSettings(source, host);
    }
}
