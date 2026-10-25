using System.Globalization;
using System.Net;
using System.Text;
using Trofeo.Modules.Clock;

namespace Trofeo.Modules.Clock.Tests;

public class OpenMeteoClientTests
{
    /// <summary>Gestionnaire factice : enregistre l'adresse demandée et renvoie une réponse écrite à la main.</summary>
    private sealed class FakeHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? RequestedUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUrl = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(respond());
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (OpenMeteoClient Client, FakeHandler Handler) Make(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(() => Json(json, status));
        return (new OpenMeteoClient(new HttpClient(handler)), handler);
    }

    private const string Forecast = """{"current":{"time":"2026-10-24T14:30","temperature_2m":12.4,"weather_code":2,"is_day":1}}""";

    [Fact]
    public async Task GetCurrent_BuildsExactUrl_AndParses()
    {
        var (client, handler) = Make(Forecast);

        var now = await client.GetCurrentAsync(new WeatherLocation("Lyon", 45.76, 4.84), TemperatureUnit.Celsius, CancellationToken.None);

        Assert.Equal(
            "https://api.open-meteo.com/v1/forecast?latitude=45.76&longitude=4.84&current=temperature_2m,weather_code,is_day&temperature_unit=celsius&timezone=auto",
            handler.RequestedUrl);
        Assert.Equal(new WeatherNow(12.4, 2, true), now);
    }

    [Fact]
    public async Task GetCurrent_Fahrenheit_UsesUnitParameter()
    {
        var (client, handler) = Make(Forecast);

        await client.GetCurrentAsync(new WeatherLocation("Lyon", 45.76, 4.84), TemperatureUnit.Fahrenheit, CancellationToken.None);

        Assert.Contains("&temperature_unit=fahrenheit&", handler.RequestedUrl);
    }

    [Fact]
    public async Task GetCurrent_Night_ParsesIsDayAsFalse()
    {
        var (client, _) = Make("""{"current":{"temperature_2m":-3,"weather_code":0,"is_day":0}}""");

        var now = await client.GetCurrentAsync(new WeatherLocation("Lyon", 45.76, 4.84), TemperatureUnit.Celsius, CancellationToken.None);

        Assert.Equal(new WeatherNow(-3, 0, false), now);
    }

    [Fact]
    public async Task GetCurrent_Coordinates_AreFormattedInvariantly()
    {
        var (client, handler) = Make(Forecast);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            await client.GetCurrentAsync(new WeatherLocation("Buenos Aires", -34.6, -58.38), TemperatureUnit.Celsius, CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Contains("latitude=-34.6&longitude=-58.38&", handler.RequestedUrl);
    }

    [Fact]
    public async Task GetCurrent_HttpError_ThrowsWeatherException()
    {
        var (client, _) = Make("{}", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<WeatherException>(() =>
            client.GetCurrentAsync(new WeatherLocation("Lyon", 45.76, 4.84), TemperatureUnit.Celsius, CancellationToken.None));
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("{}")]
    [InlineData("""{"current":{"temperature_2m":"chaud","weather_code":2,"is_day":1}}""")]
    [InlineData("""{"current":{"temperature_2m":12.4}}""")]
    public async Task GetCurrent_BadJson_ThrowsWeatherException(string json)
    {
        var (client, _) = Make(json);

        await Assert.ThrowsAsync<WeatherException>(() =>
            client.GetCurrentAsync(new WeatherLocation("Lyon", 45.76, 4.84), TemperatureUnit.Celsius, CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrent_NetworkFailure_ThrowsWeatherException()
    {
        var handler = new FakeHandler(() => throw new HttpRequestException("réseau coupé"));
        var client = new OpenMeteoClient(new HttpClient(handler));

        await Assert.ThrowsAsync<WeatherException>(() =>
            client.GetCurrentAsync(new WeatherLocation("Lyon", 45.76, 4.84), TemperatureUnit.Celsius, CancellationToken.None));
    }

    /// <summary>Gestionnaire qui ne répond jamais, jusqu'à l'annulation de la requête.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("inatteignable");
        }
    }

    [Fact]
    public async Task GetCurrent_Timeout_ThrowsWeatherException()
    {
        var client = new OpenMeteoClient(new HttpClient(new HangingHandler())) { RequestTimeout = TimeSpan.FromMilliseconds(50) };

        var ex = await Assert.ThrowsAsync<WeatherException>(() =>
            client.GetCurrentAsync(new WeatherLocation("Lyon", 45.76, 4.84), TemperatureUnit.Celsius, CancellationToken.None));
        Assert.Contains("à temps", ex.Message);
    }

    [Fact]
    public async Task SearchCity_CallerCancelsWhileWaiting_PropagatesCancellation()
    {
        var client = new OpenMeteoClient(new HttpClient(new HangingHandler()));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SearchCityAsync("Lyon", cts.Token));
    }

    [Fact]
    public async Task GetCurrent_CallerCancellation_Propagates()
    {
        var (client, _) = Make(Forecast);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetCurrentAsync(new WeatherLocation("Lyon", 45.76, 4.84), TemperatureUnit.Celsius, cts.Token));
    }

    [Fact]
    public async Task SearchCity_ParsesResults()
    {
        var (client, handler) = Make("""
            {"results":[
              {"name":"Lyon","admin1":"Auvergne-Rhône-Alpes","country":"France","latitude":45.74846,"longitude":4.84671},
              {"name":"Lyon","latitude":1.5,"longitude":2.5}
            ]}
            """);

        var cities = await client.SearchCityAsync("Lyon", CancellationToken.None);

        Assert.Equal("https://geocoding-api.open-meteo.com/v1/search?name=Lyon&count=5&language=fr&format=json", handler.RequestedUrl);
        Assert.Equal(
            [new CitySuggestion("Lyon", "Auvergne-Rhône-Alpes", "France", 45.74846, 4.84671),
             new CitySuggestion("Lyon", null, null, 1.5, 2.5)],
            cities);
    }

    [Fact]
    public async Task SearchCity_NoResults_ReturnsEmpty()
    {
        var (client, _) = Make("""{"generationtime_ms":0.5}""");

        var cities = await client.SearchCityAsync("Zzzzzz", CancellationToken.None);

        Assert.Empty(cities);
    }

    [Fact]
    public async Task SearchCity_EscapesName()
    {
        var (client, handler) = Make("{}");

        await client.SearchCityAsync("Saint-Étienne", CancellationToken.None);

        Assert.Equal(
            "https://geocoding-api.open-meteo.com/v1/search?name=Saint-%C3%89tienne&count=5&language=fr&format=json",
            handler.RequestedUrl);
    }

    [Fact]
    public async Task SearchCity_HttpError_ThrowsWeatherException()
    {
        var (client, _) = Make("{}", HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<WeatherException>(() => client.SearchCityAsync("Lyon", CancellationToken.None));
    }
}
