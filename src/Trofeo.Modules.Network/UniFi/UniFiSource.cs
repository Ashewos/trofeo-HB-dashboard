using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Network;

/// <summary>Échec d'un appel à la console UniFi : console injoignable, trop lente, statut inattendu ou réponse illisible.</summary>
internal sealed class UniFiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Débit de la passerelle d'une console UniFi, lu par son API d'intégration locale (clé API dans l'en-tête
/// <c>X-API-KEY</c>). Le site et la passerelle sont cherchés à la première lecture, puis gardés en mémoire jusqu'à un
/// 404. La console présente un certificat auto-signé : son empreinte est épinglée par <see cref="CertificatePinning"/>.
/// La source possède son <see cref="HttpClient"/> et le libère. Appelée par le module sans chevauchement, donc sans
/// verrou ; la clé API n'est jamais écrite dans le journal.
/// </summary>
public sealed partial class UniFiSource : IThroughputSource
{
    internal const string KeyName = "unifi.apiKey";
    internal const string PinKeyPrefix = "unifi.certSha256.";

    private const string BasePath = "/proxy/network/integration/v1/";

    private const string NoHostMessage = "Indique l'adresse de la console UniFi dans le logiciel de réglage";
    private const string NoKeyMessage = "Ajoute la clé API UniFi dans le logiciel de réglage";
    private const string RefusedMessage = "Clé API UniFi refusée";
    private const string NoGatewayMessage = "Aucune passerelle UniFi trouvée";
    private const string NoSiteMessage = "Aucun site UniFi trouvé";
    private const string CertificateMessage = "Certificat UniFi changé : vérifiez la console";

    private static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly string? _host;
    private readonly Uri? _baseUri;
    private readonly ISecretStore _secrets;
    private readonly IModuleLog _log;
    private readonly TimeProvider _time;
    private readonly HttpClient _http;

    private string? _apiKey;
    private Gateway? _gateway;
    private string? _lastWarning;
    private volatile bool _certificateRefused;

    private sealed record Gateway(string SiteId, string DeviceId);

    private sealed record Reply(HttpStatusCode Status, string Body)
    {
        public bool IsAuthFailure => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    /// <param name="host">Adresse de la console : nom, IPv4 ou IPv6 entre crochets, avec un port facultatif. Toute autre forme équivaut à « pas d'hôte ».</param>
    /// <param name="handler">Gestionnaire HTTP (tests) ; sans lui, un gestionnaire qui n'accepte que le certificat épinglé.</param>
    public UniFiSource(string? host, ISecretStore secrets, IModuleLog log, HttpMessageHandler? handler = null, TimeProvider? time = null)
    {
        _secrets = secrets;
        _log = log;
        _time = time ?? TimeProvider.System;
        _host = ValidateHost(host);
        if (_host is not null) _baseUri = new Uri($"https://{_host}{BasePath}");

        handler ??= CreateDefaultHandler((_, certificate, _, errors) => ValidateCertificate(certificate, errors));
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }; // le délai de chaque requête est le sien (5 s)
    }

    public string Title => "UniFi";

    public TimeSpan Interval => ReadInterval;

    public async Task<SourceReading> ReadAsync(CancellationToken ct)
    {
        _certificateRefused = false;
        if (_baseUri is null) return NotConfigured(NoHostMessage);

        var key = _apiKey;
        if (key is null)
        {
            var stored = _secrets.Get(KeyName)?.Trim();
            if (string.IsNullOrEmpty(stored)) return NotConfigured(NoKeyMessage);
            // Une clé qui ne tient pas dans un en-tête HTTP est refusée d'avance : l'erreur de format pourrait la citer.
            if (!stored.All(static c => c is > ' ' and <= '~')) return NotConfigured(RefusedMessage);
            key = _apiKey = stored;
        }

        try
        {
            var reading = await ReadRatesAsync(key, ct).ConfigureAwait(false);
            if (reading.Sample is not null) _lastWarning = null;
            return reading;
        }
        catch (UniFiException) when (_certificateRefused)
        {
            Warn("Certificat UniFi différent de celui mémorisé : connexion refusée.");
            return new SourceReading(null, new ModuleStatus(ModuleHealth.Offline, CertificateMessage));
        }
    }

    public void Dispose() => _http.Dispose();

    // --- Débit ---

    private async Task<SourceReading> ReadRatesAsync(string key, CancellationToken ct)
    {
        // Au plus une nouvelle recherche de la passerelle par lecture, quand la console répond 404 aux statistiques.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (_gateway is null && await DiscoverAsync(key, ct).ConfigureAwait(false) is { } notFound) return notFound;

            var gateway = _gateway!;
            var path = $"sites/{Uri.EscapeDataString(gateway.SiteId)}/devices/{Uri.EscapeDataString(gateway.DeviceId)}/statistics/latest";
            var reply = await GetAsync(path, key, ct).ConfigureAwait(false);
            if (reply.IsAuthFailure) return Refused();
            if (reply.Status == HttpStatusCode.NotFound)
            {
                _gateway = null;
                continue;
            }

            Expect(reply);
            using var doc = Parse(reply.Body);
            if (!doc.RootElement.TryGetProperty("uplink", out var uplink) || uplink.ValueKind != JsonValueKind.Object
                || ReadRate(uplink, "rxRateBps") is not { } down || ReadRate(uplink, "txRateBps") is not { } up)
            {
                throw new UniFiException("Débit de la console UniFi illisible.");
            }

            // L'API donne des bits par seconde ; le module affiche des octets par seconde.
            return new SourceReading(new ThroughputSample(down / 8d, up / 8d), new ModuleStatus(ModuleHealth.Online));
        }

        _log.Warning("Passerelle UniFi introuvable après une nouvelle recherche.");
        return new SourceReading(null, new ModuleStatus(ModuleHealth.Offline));
    }

    /// <summary>Cherche le premier site puis sa passerelle. Renvoie l'état à afficher s'il n'y en a pas, sinon <c>null</c> (et <c>_gateway</c> est renseigné).</summary>
    private async Task<SourceReading?> DiscoverAsync(string key, CancellationToken ct)
    {
        var sites = await GetAsync("sites", key, ct).ConfigureAwait(false);
        if (sites.IsAuthFailure) return Refused();
        Expect(sites);

        string siteId;
        using (var doc = Parse(sites.Body))
        {
            var data = Data(doc.RootElement);
            if (data.GetArrayLength() == 0) return new SourceReading(null, new ModuleStatus(ModuleHealth.Offline, NoSiteMessage));
            siteId = ReadId(data[0]) ?? throw new UniFiException("Site de la console UniFi illisible.");
        }

        var devices = await GetAsync($"sites/{Uri.EscapeDataString(siteId)}/devices?limit=200", key, ct).ConfigureAwait(false);
        if (devices.IsAuthFailure) return Refused();
        Expect(devices);

        using (var doc = Parse(devices.Body))
        {
            foreach (var device in Data(doc.RootElement).EnumerateArray())
            {
                if (!HasGatewayFeature(device) || ReadId(device) is not { } deviceId) continue;
                _gateway = new Gateway(siteId, deviceId);
                return null;
            }
        }

        return new SourceReading(null, new ModuleStatus(ModuleHealth.Offline, NoGatewayMessage));
    }

    private static bool HasGatewayFeature(JsonElement device)
    {
        if (device.ValueKind != JsonValueKind.Object
            || !device.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (var feature in features.EnumerateArray())
        {
            if (feature.ValueKind == JsonValueKind.String && string.Equals(feature.GetString(), "gateway", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? ReadId(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(id.GetString())
            ? id.GetString()
            : null;

    /// <summary>Débit en bits par seconde : un nombre fini et positif ou nul, sinon <c>null</c>.</summary>
    private static double? ReadRate(JsonElement uplink, string name) =>
        uplink.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var rate) && double.IsFinite(rate) && rate >= 0
            ? rate
            : null;

    private static JsonElement Data(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data
            : throw new UniFiException("Réponse de la console UniFi illisible.");

    private SourceReading Refused()
    {
        _apiKey = null; // la clé est relue du coffre à la lecture suivante : une clé corrigée dans le réglage est reprise
        Warn("Clé API UniFi refusée par la console.");
        return NotConfigured(RefusedMessage);
    }

    private static SourceReading NotConfigured(string message) =>
        new(null, new ModuleStatus(ModuleHealth.NotConfigured, message));

    /// <summary>Journalise une fois par épisode : la même alerte n'est pas répétée à chaque lecture.</summary>
    private void Warn(string message)
    {
        if (message == _lastWarning) return;
        _lastWarning = message;
        _log.Warning(message);
    }

    // --- HTTP ---

    private async Task<Reply> GetAsync(string path, string key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri!, path));
        request.Headers.Add("X-API-KEY", key);

        using var timeout = new CancellationTokenSource(RequestTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            using var response = await _http.SendAsync(request, linked.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            return new Reply(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new UniFiException($"La console UniFi n'a pas répondu en {RequestTimeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException ex)
        {
            throw new UniFiException($"Console UniFi injoignable : {ex.Message}", ex);
        }
    }

    /// <summary>Lève pour tout statut autre que 2xx : les statuts attendus (401, 403, 404) sont traités avant.</summary>
    private static void Expect(Reply reply)
    {
        if ((int)reply.Status is < 200 or > 299)
        {
            throw new UniFiException($"Console UniFi : statut HTTP {(int)reply.Status} inattendu.");
        }
    }

    private static JsonDocument Parse(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new UniFiException("Réponse de la console UniFi illisible.", ex);
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
        doc.Dispose();
        throw new UniFiException("Réponse de la console UniFi illisible.");
    }

    // --- Hôte ---

    /// <summary>
    /// L'hôte tel que saisi (sans espaces autour) s'il est un nom, une IPv4 ou une IPv6 entre crochets, avec un port
    /// facultatif ; <c>null</c> sinon (schéma, chemin, identifiants, espaces… ne sont jamais acceptés).
    /// </summary>
    internal static string? ValidateHost(string? host)
    {
        var text = host?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 260) return null;

        var match = HostPattern().Match(text);
        if (!match.Success) return null;

        if (match.Groups["v6"].Success
            && !(IPAddress.TryParse(match.Groups["v6"].Value, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6))
        {
            return null;
        }
        if (match.Groups["name"].Success && match.Groups["name"].Length > 253) return null;
        if (match.Groups["port"].Success && !(int.TryParse(match.Groups["port"].Value, out var port) && port is >= 1 and <= 65535)) return null;
        return text;
    }

    [GeneratedRegex(
        @"\A(?:(?<name>[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*)|\[(?<v6>[0-9A-Fa-f:.]+)\])(?::(?<port>[0-9]{1,5}))?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    // --- Certificat ---

    /// <summary>Gestionnaire HTTP de production : pas de redirection (la clé API ne part que vers la console) et certificat épinglé.</summary>
    internal static SocketsHttpHandler CreateDefaultHandler(RemoteCertificateValidationCallback validate) =>
        new()
        {
            AllowAutoRedirect = false,
            SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = validate },
        };

    /// <summary>Empreinte SHA-256 du certificat, en hexadécimal majuscule.</summary>
    internal static string Fingerprint(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

    /// <summary>Validation du certificat présenté par la console (appelée par la pile TLS).</summary>
    internal bool ValidateCertificate(X509Certificate? certificate, SslPolicyErrors errors) =>
        certificate is not null && ValidateFingerprint(Fingerprint(certificate), errors);

    /// <summary>
    /// Épinglage sous <c>unifi.certSha256.{hôte}</c> par <see cref="CertificatePinning.Check"/>. Un refus est noté pour
    /// que la lecture en échec devienne « Certificat changé » plutôt qu'une panne réseau.
    /// </summary>
    internal bool ValidateFingerprint(string presentedSha256, SslPolicyErrors errors)
    {
        if (_host is null) return false;

        var pinKey = PinKeyPrefix + _host;
        var (accept, newPin) = CertificatePinning.Check(_secrets.Get(pinKey), presentedSha256, errors);
        if (newPin is not null)
        {
            _secrets.Set(pinKey, newPin);
            _log.Info("Certificat de la console UniFi mémorisé.");
        }
        if (!accept) _certificateRefused = true;
        return accept;
    }
}
