using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Trofeo.Modules.Network;

/// <summary>Échec d'un appel à la Freebox : box injoignable, trop lente ou réponse illisible.</summary>
internal sealed class FreeboxException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Enveloppe des réponses de l'API : <c>{success, result, error_code, msg}</c>. La box répond parfois les erreurs
/// d'authentification en HTTP 403 avec cette même enveloppe : le statut HTTP n'est donc pas pris en compte.
/// <c>Result</c> peut porter des jetons : <see cref="ToString"/> ne l'imprime pas.
/// </summary>
internal sealed record FreeboxResponse(bool Success, string? ErrorCode, JsonElement Result)
{
    /// <summary>Ni le résultat ni les jetons qu'il peut contenir ne sortent par <c>ToString</c> (journaux, messages de test).</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Success = ").Append(Success).Append(", ErrorCode = ").Append(ErrorCode);
        return true;
    }

    public bool IsError(string code) => !Success && ErrorCode == code;

    public string? GetString(string name) =>
        Result.ValueKind == JsonValueKind.Object && Result.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public double? GetNumber(string name) =>
        Result.ValueKind == JsonValueKind.Object && Result.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number
            : null;
}

/// <summary>Version de l'API annoncée par la box : préfixe de base (« /api/ ») et version majeure.</summary>
internal sealed record FreeboxApiInfo(string BaseUrl, int Major);

/// <summary>
/// Appels HTTP à la Freebox. Chaque requête a son propre délai de 5 s, porté par un jeton lié plutôt que par
/// <see cref="HttpClient.Timeout"/>, qui est celui de l'hôte. Toute panne devient une <see cref="FreeboxException"/>.
/// </summary>
internal sealed class FreeboxApi(HttpClient http, Uri baseUri, TimeProvider time)
{
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions BodyOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary><c>GET /api_version</c> : la seule réponse sans enveloppe.</summary>
    public async Task<FreeboxApiInfo> DiscoverAsync(CancellationToken ct)
    {
        using var doc = Parse(await SendAsync(HttpMethod.Get, "/api_version", null, null, ct).ConfigureAwait(false));
        var root = doc.RootElement;
        if (!root.TryGetProperty("api_base_url", out var baseUrl) || baseUrl.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("api_version", out var version) || version.ValueKind != JsonValueKind.String
            || !int.TryParse(version.GetString()!.Split('.')[0], out var major))
        {
            throw new FreeboxException("Version d'API Freebox illisible.");
        }

        var prefix = baseUrl.GetString()!;
        if (!prefix.EndsWith('/')) prefix += "/";
        Resolve(prefix); // refuse un préfixe qui mène ailleurs que sur la box
        return new FreeboxApiInfo(prefix, major);
    }

    public async Task<FreeboxResponse> GetAsync(string path, string? sessionToken, CancellationToken ct) =>
        Envelope(await SendAsync(HttpMethod.Get, path, null, sessionToken, ct).ConfigureAwait(false));

    public async Task<FreeboxResponse> PostAsync(string path, object body, CancellationToken ct) =>
        Envelope(await SendAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body, BodyOptions), null, ct).ConfigureAwait(false));

    /// <summary>Adresse absolue d'un chemin de la box ; lève si elle sort de l'hôte configuré.</summary>
    private Uri Resolve(string path)
    {
        if (!Uri.TryCreate(baseUri, path, out var uri) || uri.Scheme != baseUri.Scheme || uri.Authority != baseUri.Authority)
        {
            throw new FreeboxException("Adresse d'API Freebox inattendue.");
        }
        return uri;
    }

    private async Task<string> SendAsync(HttpMethod method, string path, string? json, string? sessionToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, Resolve(path));
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (sessionToken is not null) request.Headers.Add("X-Fbx-App-Auth", sessionToken);

        using var timeout = new CancellationTokenSource(RequestTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            using var response = await http.SendAsync(request, linked.Token).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FreeboxException($"La Freebox n'a pas répondu en {RequestTimeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException ex)
        {
            throw new FreeboxException($"Freebox injoignable : {ex.Message}", ex);
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
            throw new FreeboxException("Réponse de la Freebox illisible.", ex);
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
        doc.Dispose();
        throw new FreeboxException("Réponse de la Freebox illisible.");
    }

    private static FreeboxResponse Envelope(string body)
    {
        using var doc = Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("success", out var success) || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new FreeboxException("Réponse de la Freebox illisible.");
        }

        var errorCode = root.TryGetProperty("error_code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;
        var result = root.TryGetProperty("result", out var value) ? value.Clone() : default;
        return new FreeboxResponse(success.GetBoolean(), errorCode, result);
    }
}
