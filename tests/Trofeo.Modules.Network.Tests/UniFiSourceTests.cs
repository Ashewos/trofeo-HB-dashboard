using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Trofeo.ModuleKit;
using Trofeo.TestDoubles;

namespace Trofeo.Modules.Network.Tests;

public class UniFiSourceTests
{
    private const string Host = "192.168.1.1";
    private const string ApiKey = "API-KEY-SECRET-0123456789";
    private const string NoHostMessage = "Indique l'adresse de la console UniFi dans le logiciel de réglage";
    private const string NoKeyMessage = "Ajoute la clé API UniFi dans le logiciel de réglage";
    private const string RefusedMessage = "Clé API UniFi refusée";
    private const string NoGatewayMessage = "Aucune passerelle UniFi trouvée";
    private const string CertMessage = "Certificat UniFi changé : vérifie la console";
    private const string StatsUnavailableMessage = "Statistiques UniFi indisponibles pour cette passerelle";

    private const string Prefix = "/proxy/network/integration/v1/";
    private const string Sites = "GET " + Prefix + "sites";
    private const string Devices = "GET " + Prefix + "sites/site-1/devices?limit=200";
    private const string Stats = "GET " + Prefix + "sites/site-1/devices/gw-1/statistics/latest";

    private const string SitesJson = """{"data":[{"id":"site-1","name":"Default"},{"id":"site-2","name":"Autre"}],"count":2}""";
    private const string DevicesJson =
        """{"data":[{"id":"sw-1","name":"Switch","features":["switching"]},{"id":"gw-1","name":"UDM","features":["gateway","switching"]}]}""";
    private const string StatsJson = """{"uplink":{"rxRateBps":400000000,"txRateBps":80000000}}""";

    private sealed record Call(string Route, string Host, int Port, string? ApiKey);

    private sealed record Hang;

    /// <summary>Console factice : chaque route (« GET /chemin?requête ») a sa file de réponses ; la dernière se répète.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<object>> _routes = [];

        public List<Call> Calls { get; } = [];
        public TaskCompletionSource Started { get; } = new();
        public int Disposed { get; private set; }

        /// <summary>Appelé à chaque requête, avant la réponse : sert à simuler la validation du certificat.</summary>
        public Action? OnRequest { get; set; }

        public void On(string route, string json, HttpStatusCode status = HttpStatusCode.OK) => Add(route, (status, json));
        public void OnFail(string route, Exception error) => Add(route, error);
        public void OnHang(string route) => Add(route, new Hang());
        public void Clear(string route) => _routes.Remove(route);
        public int Count(string route) => Calls.Count(c => c.Route == route);

        private void Add(string route, object entry)
        {
            if (!_routes.TryGetValue(route, out var queue)) _routes[route] = queue = new Queue<object>();
            queue.Enqueue(entry);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            var route = $"{request.Method} {uri.PathAndQuery}";
            request.Headers.TryGetValues("X-API-KEY", out var key);
            Calls.Add(new Call(route, uri.Host, uri.Port, key?.SingleOrDefault()));
            Started.TrySetResult();
            OnRequest?.Invoke();

            if (!_routes.TryGetValue(route, out var queue) || queue.Count == 0)
            {
                throw new HttpRequestException($"pas de réponse prévue pour {route}");
            }

            var entry = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            switch (entry)
            {
                case Exception error:
                    throw error;
                case Hang:
                    await Task.Delay(Timeout.Infinite, ct);
                    throw new InvalidOperationException("inatteignable");
                default:
                    var (status, json) = ((HttpStatusCode, string))entry;
                    return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
        }

        protected override void Dispose(bool disposing)
        {
            Disposed++;
            base.Dispose(disposing);
        }
    }

    private sealed class CountingSecretStore : ISecretStore
    {
        private readonly FakeSecretStore _inner = new();
        public Dictionary<string, int> Gets { get; } = [];

        public string? Get(string key)
        {
            Gets[key] = Gets.GetValueOrDefault(key) + 1;
            return _inner.Get(key);
        }

        public void Set(string key, string? value) => _inner.Set(key, value);
    }

    private sealed class Rig : IDisposable
    {
        public ScriptedHandler Handler { get; } = new();
        public CountingSecretStore Secrets { get; } = new();
        public FakeModuleLog Log { get; } = new();
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
        public UniFiSource Source { get; }

        public Rig(string? host = Host, string? apiKey = ApiKey, bool script = true)
        {
            if (apiKey is not null) Secrets.Set("unifi.apiKey", apiKey);
            if (script)
            {
                Handler.On(Sites, SitesJson);
                Handler.On(Devices, DevicesJson);
                Handler.On(Stats, StatsJson);
            }
            Source = new UniFiSource(host, Secrets, Log, Handler, Time);
        }

        public Task<SourceReading> Read(CancellationToken ct = default) => Source.ReadAsync(ct);

        public void Dispose() => Source.Dispose();
    }

    // --- Configuration ---

    [Fact]
    public void Title_And_Interval()
    {
        using var rig = new Rig();

        Assert.Equal("UniFi", rig.Source.Title);
        Assert.Equal(TimeSpan.FromSeconds(5), rig.Source.Interval);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://192.168.1.1")]
    [InlineData("192.168.1.1/proxy")]
    [InlineData("admin@192.168.1.1")]
    [InlineData("192.168.1.1 :443")]
    [InlineData("unifi local")]
    [InlineData("unifi.local:")]
    [InlineData("unifi.local:99999")]
    [InlineData("unifi.local:abc")]
    [InlineData("[fe80::1")]
    [InlineData("[not-an-address]")]
    [InlineData("fe80::1")]
    [InlineData("-unifi.local")]
    [InlineData("unifi..local")]
    [InlineData("unifi.local?x=1")]
    [InlineData("unifi.local#a")]
    public async Task NoHost_NotConfigured(string? host)
    {
        using var rig = new Rig(host);

        var reading = await rig.Read();

        Assert.Equal(new ModuleStatus(ModuleHealth.NotConfigured, NoHostMessage), reading.Status);
        Assert.Null(reading.Sample);
        Assert.Empty(rig.Handler.Calls);
    }

    [Theory]
    [InlineData("unifi.local", "unifi.local", 443)]
    [InlineData("my-unifi", "my-unifi", 443)]
    [InlineData("192.168.1.1", "192.168.1.1", 443)]
    [InlineData("192.168.1.1:8443", "192.168.1.1", 8443)]
    [InlineData("  192.168.1.1  ", "192.168.1.1", 443)]
    [InlineData("[fe80::1]", "[fe80::1]", 443)]
    [InlineData("[2001:db8::1]:8443", "[2001:db8::1]", 8443)]
    public async Task ValidHosts_AreAccepted(string host, string expectedHost, int expectedPort)
    {
        using var rig = new Rig(host);

        var reading = await rig.Read();

        Assert.Equal(ModuleHealth.Online, reading.Status.Health);
        Assert.All(rig.Handler.Calls, c =>
        {
            Assert.Equal(expectedHost, c.Host);
            Assert.Equal(expectedPort, c.Port);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoKey_NotConfigured(string? key)
    {
        using var rig = new Rig(apiKey: key);

        var reading = await rig.Read();

        Assert.Equal(new ModuleStatus(ModuleHealth.NotConfigured, NoKeyMessage), reading.Status);
        Assert.Null(reading.Sample);
        Assert.Empty(rig.Handler.Calls);
    }

    [Fact]
    public async Task NoHost_TakesPrecedenceOverNoKey()
    {
        using var rig = new Rig(host: null, apiKey: null);

        var reading = await rig.Read();

        Assert.Equal(NoHostMessage, reading.Status.ActionMessage);
    }

    [Fact]
    public async Task KeyAddedLater_IsPickedUp()
    {
        using var rig = new Rig(apiKey: null);
        await rig.Read();

        rig.Secrets.Set("unifi.apiKey", ApiKey);
        var reading = await rig.Read();

        Assert.Equal(ModuleHealth.Online, reading.Status.Health);
    }

    [Fact]
    public async Task KeyWithControlCharacters_IsRefusedWithoutBeingSent()
    {
        using var rig = new Rig(apiKey: "bad\r\nkey");

        var reading = await rig.Read();

        Assert.Equal(new ModuleStatus(ModuleHealth.NotConfigured, RefusedMessage), reading.Status);
        Assert.Empty(rig.Handler.Calls);
    }

    // --- Lecture ---

    [Fact]
    public async Task FindsGatewayAndReadsUplink()
    {
        using var rig = new Rig();

        var reading = await rig.Read();

        Assert.Equal(new ModuleStatus(ModuleHealth.Online), reading.Status);
        Assert.Equal(new ThroughputSample(50_000_000, 10_000_000), reading.Sample); // bits/s ÷ 8
        Assert.Equal([Sites, Devices, Stats], rig.Handler.Calls.Select(c => c.Route));
        Assert.All(rig.Handler.Calls, c => Assert.Equal(ApiKey, c.ApiKey));
    }

    [Fact]
    public async Task UcgWithoutGatewayFeature_IsAccepted()
    {
        using var rig = new Rig();
        rig.Handler.Clear(Devices);
        rig.Handler.On(Devices, """{"data":[{"id":"gw-1","name":"UCG","model":"UCG Fiber","features":["switching"]},{"id":"ap-1","model":"U7 Lite","features":["accessPoint"]}]}""");

        var reading = await rig.Read();

        Assert.Equal(ModuleHealth.Online, reading.Status.Health);
        Assert.Equal(new ThroughputSample(50_000_000, 10_000_000), reading.Sample);
        Assert.Equal([Sites, Devices, Stats], rig.Handler.Calls.Select(c => c.Route));
    }

    [Fact]
    public async Task SecondRead_UsesCachedIdsAndKey()
    {
        using var rig = new Rig();
        await rig.Read();
        rig.Handler.Clear(Stats);
        rig.Handler.On(Stats, """{"uplink":{"rxRateBps":8000,"txRateBps":16000}}""");

        var reading = await rig.Read();

        Assert.Equal(new ThroughputSample(1000, 2000), reading.Sample);
        Assert.Equal([Sites, Devices, Stats, Stats], rig.Handler.Calls.Select(c => c.Route));
        Assert.Equal(1, rig.Secrets.Gets["unifi.apiKey"]);
    }

    [Fact]
    public async Task NoGateway_Offline()
    {
        using var rig = new Rig();
        rig.Handler.Clear(Devices);
        rig.Handler.On(Devices, """{"data":[{"id":"sw-1","features":["switching"]},{"id":"ap-1"},{"id":"x","features":"gateway"}]}""");

        var reading = await rig.Read();

        Assert.Equal(new ModuleStatus(ModuleHealth.Offline, NoGatewayMessage), reading.Status);
        Assert.Null(reading.Sample);
        Assert.Equal(0, rig.Handler.Count(Stats));
    }

    [Fact]
    public async Task NoSite_Offline()
    {
        using var rig = new Rig();
        rig.Handler.Clear(Sites);
        rig.Handler.On(Sites, """{"data":[]}""");

        var reading = await rig.Read();

        Assert.Equal(ModuleHealth.Offline, reading.Status.Health);
        Assert.Null(reading.Sample);
        Assert.Equal(0, rig.Handler.Count(Devices));
    }

    [Fact]
    public async Task NoGateway_IsSearchedAgainOnTheNextRead()
    {
        using var rig = new Rig();
        rig.Handler.Clear(Devices);
        rig.Handler.On(Devices, """{"data":[]}""");
        await rig.Read();
        rig.Handler.Clear(Devices);
        rig.Handler.On(Devices, DevicesJson);

        var reading = await rig.Read();

        Assert.Equal(ModuleHealth.Online, reading.Status.Health);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Unauthorized_NotConfigured(HttpStatusCode status)
    {
        using var rig = new Rig();
        rig.Handler.Clear(Sites);
        rig.Handler.On(Sites, """{"statusCode":401,"message":"Unauthorized"}""", status);

        var reading = await rig.Read();

        Assert.Equal(new ModuleStatus(ModuleHealth.NotConfigured, RefusedMessage), reading.Status);
        Assert.Null(reading.Sample);
    }

    [Fact]
    public async Task Unauthorized_OnLaterStep_AlsoNotConfigured()
    {
        using var rig = new Rig();
        await rig.Read();
        rig.Handler.Clear(Stats);
        rig.Handler.On(Stats, "{}", HttpStatusCode.Unauthorized);

        var reading = await rig.Read();

        Assert.Equal(new ModuleStatus(ModuleHealth.NotConfigured, RefusedMessage), reading.Status);
    }

    [Fact]
    public async Task Unauthorized_ReReadsTheKeyFromTheStore()
    {
        using var rig = new Rig();
        rig.Handler.Clear(Stats);
        rig.Handler.On(Stats, "{}", HttpStatusCode.Unauthorized);
        await rig.Read();
        Assert.Equal(1, rig.Secrets.Gets["unifi.apiKey"]);

        rig.Secrets.Set("unifi.apiKey", "FIXED-KEY");
        rig.Handler.Clear(Stats);
        rig.Handler.On(Stats, StatsJson);
        var reading = await rig.Read();
        await rig.Read();

        Assert.Equal(ModuleHealth.Online, reading.Status.Health);
        Assert.Equal("FIXED-KEY", rig.Handler.Calls[^1].ApiKey);
        Assert.Equal(2, rig.Secrets.Gets["unifi.apiKey"]); // relue une fois après le refus, puis gardée
    }

    [Fact]
    public async Task NotFound_Rediscovers()
    {
        using var rig = new Rig();
        await rig.Read();
        rig.Handler.Clear(Sites);
        rig.Handler.On(Sites, """{"data":[{"id":"site-1"}]}""");
        rig.Handler.Clear(Devices);
        rig.Handler.On(Devices, """{"data":[{"id":"gw-1","features":["gateway"]}]}""");
        rig.Handler.Clear(Stats);
        rig.Handler.On(Stats, "{}", HttpStatusCode.NotFound);
        rig.Handler.On(Stats, """{"uplink":{"rxRateBps":800,"txRateBps":80}}""");
        var before = rig.Handler.Calls.Count;

        var reading = await rig.Read();

        Assert.Equal(new ThroughputSample(100, 10), reading.Sample);
        Assert.Equal([Stats, Sites, Devices, Stats], rig.Handler.Calls.Skip(before).Select(c => c.Route));
    }

    [Fact]
    public async Task NotFound_Twice_IsOfflineWithoutLoopingOrThrowing()
    {
        using var rig = new Rig();
        rig.Handler.Clear(Stats);
        rig.Handler.On(Stats, "{}", HttpStatusCode.NotFound);

        var reading = await rig.Read();

        Assert.Equal(ModuleHealth.Offline, reading.Status.Health);
        Assert.Equal(StatsUnavailableMessage, reading.Status.ActionMessage);
        Assert.Null(reading.Sample);
        Assert.Equal(2, rig.Handler.Count(Stats)); // une seule nouvelle recherche
        Assert.Equal(2, rig.Handler.Count(Sites));
        Assert.Single(rig.Log.Entries, e => e.StartsWith("Warning") && e.Contains("introuvable"));
    }

    [Fact]
    public async Task NotFound_Twice_BacksOffForAMinute_WithoutHttpAndWithoutLogFlood()
    {
        using var rig = new Rig();
        rig.Handler.Clear(Stats);
        rig.Handler.On(Stats, "{}", HttpStatusCode.NotFound);
        var first = await rig.Read();
        var calls = rig.Handler.Calls.Count;

        for (var i = 0; i < 11; i++)
        {
            rig.Time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(first, await rig.Read());
        }

        Assert.Equal(calls, rig.Handler.Calls.Count); // 55 s : aucun appel
        Assert.Single(rig.Log.Entries, e => e.StartsWith("Warning"));

        rig.Time.Advance(TimeSpan.FromSeconds(5)); // 60 s : nouvelle recherche
        Assert.Equal(first, await rig.Read());
        Assert.Equal(calls + 6, rig.Handler.Calls.Count); // (sites, appareils, statistiques) deux fois
        Assert.Single(rig.Log.Entries, e => e.StartsWith("Warning")); // même alerte : pas répétée

        // Les statistiques reviennent : la lecture qui suit la minute de repos est en ligne.
        rig.Handler.Clear(Stats);
        rig.Handler.On(Stats, StatsJson);
        rig.Time.Advance(TimeSpan.FromSeconds(60));
        var recovered = await rig.Read();
        Assert.Equal(ModuleHealth.Online, recovered.Status.Health);
        Assert.NotNull(recovered.Sample);
    }

    [Fact]
    public async Task Rates_AreNotNegativeOrNonNumeric()
    {
        using var rig = new Rig();
        foreach (var body in new[]
        {
            """{"uplink":{"rxRateBps":-1,"txRateBps":5}}""",
            """{"uplink":{"rxRateBps":"x","txRateBps":5}}""",
            """{"uplink":{"txRateBps":5}}""",
            """{"uplink":null}""",
            """{}""",
        })
        {
            rig.Handler.Clear(Stats);
            rig.Handler.On(Stats, body);

            await Assert.ThrowsAsync<UniFiException>(() => rig.Read());
        }
    }

    // --- Pannes ---

    [Fact]
    public async Task NetworkError_Throws_AndNextReadResumes()
    {
        using var rig = new Rig();
        rig.Handler.Clear(Sites);
        rig.Handler.OnFail(Sites, new HttpRequestException("coupé"));
        rig.Handler.On(Sites, SitesJson);

        await Assert.ThrowsAsync<UniFiException>(() => rig.Read());
        var reading = await rig.Read();

        Assert.Equal(ModuleHealth.Online, reading.Status.Health);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task UnexpectedStatus_Throws(HttpStatusCode status)
    {
        using var rig = new Rig();
        rig.Handler.Clear(Sites);
        rig.Handler.On(Sites, "{}", status);

        var error = await Assert.ThrowsAsync<UniFiException>(() => rig.Read());

        Assert.Contains(((int)status).ToString(), error.Message);
    }

    [Theory]
    [InlineData("<html>502</html>")]
    [InlineData("")]
    [InlineData("[1]")]
    [InlineData("{}")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"data":[1]}""")]
    [InlineData("""{"data":[{"name":"sans id"}]}""")]
    public async Task UnparseableSites_Throws(string body)
    {
        using var rig = new Rig();
        rig.Handler.Clear(Sites);
        rig.Handler.On(Sites, body);

        await Assert.ThrowsAsync<UniFiException>(() => rig.Read());
    }

    [Fact]
    public async Task Request_HasItsOwn5SecondTimeout()
    {
        using var rig = new Rig(script: false);
        rig.Handler.OnHang(Sites);

        var read = rig.Read();
        await rig.Handler.Started.Task;
        rig.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(read.IsCompleted);
        rig.Time.Advance(TimeSpan.FromSeconds(1));

        var error = await Assert.ThrowsAsync<UniFiException>(() => read);
        Assert.Contains("5 s", error.Message);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAsCancellation()
    {
        using var rig = new Rig(script: false);
        rig.Handler.OnHang(Sites);
        using var cts = new CancellationTokenSource();

        var read = rig.Read(cts.Token);
        await rig.Handler.Started.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    // --- Certificat ---

    private static X509Certificate2 NewCertificate(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    [Fact]
    public void Fingerprint_IsSha256OfTheCertificate()
    {
        using var cert = NewCertificate("a");

        var fingerprint = UniFiSource.Fingerprint(cert);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(cert.RawData)), fingerprint);
        Assert.Equal(64, fingerprint.Length);
    }

    [Fact]
    public void Validate_PinsFirstCertificate_AcceptsItAgain_RefusesAnother()
    {
        using var rig = new Rig();
        using var first = NewCertificate("first");
        using var other = NewCertificate("other");
        const SslPolicyErrors errors = SslPolicyErrors.RemoteCertificateChainErrors;

        Assert.True(rig.Source.ValidateCertificate(first, errors));
        Assert.Equal(UniFiSource.Fingerprint(first), rig.Secrets.Get($"unifi.certSha256.{Host}"));
        Assert.True(rig.Source.ValidateCertificate(first, errors));
        Assert.False(rig.Source.ValidateCertificate(other, errors));
        Assert.Equal(UniFiSource.Fingerprint(first), rig.Secrets.Get($"unifi.certSha256.{Host}")); // l'empreinte mémorisée reste
        Assert.False(rig.Source.ValidateCertificate(null, SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    [Fact]
    public void Validate_PinKnown_KeepsItInMemory_NeverAcceptsFirstUseAgain()
    {
        using var rig = new Rig();
        using var first = NewCertificate("first");
        using var other = NewCertificate("other");
        const SslPolicyErrors errors = SslPolicyErrors.RemoteCertificateChainErrors;
        Assert.True(rig.Source.ValidateCertificate(first, errors)); // première utilisation : épinglé

        rig.Secrets.Set($"unifi.certSha256.{Host}", null); // le coffre ne rend plus rien

        Assert.False(rig.Source.ValidateCertificate(other, errors));
        Assert.True(rig.Source.ValidateCertificate(first, errors));
        Assert.Null(rig.Secrets.Get($"unifi.certSha256.{Host}")); // et on ne réécrit pas une empreinte étrangère
    }

    [Fact]
    public void Validate_PinReadFromTheVault_IsKeptInMemory()
    {
        using var rig = new Rig();
        using var first = NewCertificate("first");
        using var other = NewCertificate("other");
        const SslPolicyErrors errors = SslPolicyErrors.RemoteCertificateChainErrors;
        rig.Secrets.Set($"unifi.certSha256.{Host}", UniFiSource.Fingerprint(first));
        Assert.True(rig.Source.ValidateCertificate(first, errors));

        rig.Secrets.Set($"unifi.certSha256.{Host}", null);

        Assert.False(rig.Source.ValidateCertificate(other, errors));
        Assert.Equal(1, rig.Secrets.Gets[$"unifi.certSha256.{Host}"]); // lue une seule fois
    }

    [Fact]
    public void Validate_PinKey_UsesTheLowerCaseHost()
    {
        using var rig = new Rig("UniFi.Local:8443");
        using var cert = NewCertificate("a");

        Assert.True(rig.Source.ValidateCertificate(cert, SslPolicyErrors.RemoteCertificateChainErrors));

        Assert.Equal(UniFiSource.Fingerprint(cert), rig.Secrets.Get("unifi.certSha256.unifi.local:8443"));
        Assert.Null(rig.Secrets.Get("unifi.certSha256.UniFi.Local:8443"));
    }

    [Fact]
    public void Validate_NoErrors_AcceptsWithoutPinning()
    {
        using var rig = new Rig();
        using var cert = NewCertificate("ca-signed");

        Assert.True(rig.Source.ValidateCertificate(cert, SslPolicyErrors.None));
        Assert.Null(rig.Secrets.Get($"unifi.certSha256.{Host}"));
    }

    [Fact]
    public async Task ChangedCertificate_IsOfflineWithMessage_ThenRecoversWhenNotRefusedAnymore()
    {
        using var rig = new Rig();
        rig.Secrets.Set($"unifi.certSha256.{Host}", "AAAA");
        var refuse = true;
        rig.Handler.OnRequest = () =>
        {
            // La pile TLS interroge la validation avant que la requête parte ; un refus fait échouer la requête.
            if (refuse && !rig.Source.ValidateFingerprint("BBBB", SslPolicyErrors.RemoteCertificateChainErrors))
            {
                throw new HttpRequestException("The SSL connection could not be established");
            }
        };

        var reading = await rig.Read();

        Assert.Equal(new ModuleStatus(ModuleHealth.Offline, CertMessage), reading.Status);
        Assert.Null(reading.Sample);
        Assert.Contains(rig.Log.Entries, e => e.StartsWith("Warning"));

        refuse = false;
        var next = await rig.Read();
        Assert.Equal(ModuleHealth.Online, next.Status.Health);
    }

    [Fact]
    public async Task TransportError_WithoutCertificateRefusal_StillThrows()
    {
        using var rig = new Rig();
        rig.Handler.Clear(Sites);
        rig.Handler.OnFail(Sites, new HttpRequestException("coupé"));
        rig.Source.ValidateFingerprint("AAAA", SslPolicyErrors.RemoteCertificateChainErrors); // épingle, sans refus

        await Assert.ThrowsAsync<UniFiException>(() => rig.Read());
    }

    [Fact]
    public void DefaultHandler_DoesNotFollowRedirects_AndValidatesTheCertificate()
    {
        using var handler = UniFiSource.CreateDefaultHandler((_, _, _, _) => true);

        Assert.False(handler.AllowAutoRedirect);
        Assert.NotNull(handler.SslOptions.RemoteCertificateValidationCallback);
    }

    [Fact]
    public async Task WithoutHandler_BuildsItsOwn_AndReadsNothingWithoutKey()
    {
        var source = new UniFiSource(Host, new FakeSecretStore(), new FakeModuleLog());

        var reading = await source.ReadAsync(default); // pas de clé : aucun appel réseau

        Assert.Equal(NoKeyMessage, reading.Status.ActionMessage);
        source.Dispose();
        source.Dispose();
    }

    [Fact]
    public void Dispose_DisposesTheHandler()
    {
        var rig = new Rig();

        rig.Dispose();

        Assert.True(rig.Handler.Disposed >= 1);
    }

    // --- Journal ---

    [Fact]
    public async Task Log_NeverContainsTheApiKey()
    {
        using var rig = new Rig();
        rig.Secrets.Set($"unifi.certSha256.{Host}", "AAAA");
        rig.Handler.OnRequest = () =>
        {
            if (!rig.Source.ValidateFingerprint("BBBB", SslPolicyErrors.RemoteCertificateChainErrors))
            {
                throw new HttpRequestException("refusé");
            }
        };
        await rig.Read(); // certificat refusé : avertissement
        rig.Handler.OnRequest = null;
        rig.Handler.Clear(Sites);
        rig.Handler.On(Sites, "{}", HttpStatusCode.Unauthorized);
        await rig.Read(); // clé refusée
        rig.Secrets.Set("unifi.apiKey", ApiKey);
        rig.Handler.Clear(Sites);
        rig.Handler.On(Sites, "{}", HttpStatusCode.BadGateway);
        var descriptor = new NetworkFactory((_, _) => rig.Source).Descriptor;
        using var module = new NetworkModule(descriptor, rig.Source, rig.Time, rig.Log);
        await module.StartAsync(CancellationToken.None); // lecture qui lève : erreur journalisée par le module

        Assert.Contains(rig.Log.Entries, e => e.StartsWith("Error"));
        Assert.NotEmpty(rig.Log.Entries);
        Assert.DoesNotContain(rig.Log.Entries, e => e.Contains(ApiKey));
    }
}
