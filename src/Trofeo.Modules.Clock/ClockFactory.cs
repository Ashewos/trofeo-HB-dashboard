using System.Text.Json;
using Trofeo.ModuleKit;

namespace Trofeo.Modules.Clock;

/// <summary>Fabrique du module Heure, date et météo ; la météo vient de <paramref name="weather"/>.</summary>
public sealed class ClockFactory(IWeatherProvider weather) : IConfigurableModuleFactory
{
    public ModuleDescriptor Descriptor { get; } = new(
        "clock",
        "Heure, date et météo",
        new GridSize(3, 1),
        new Dictionary<SizePreset, GridSize>
        {
            [SizePreset.Compact] = new GridSize(3, 1),
            [SizePreset.Ideal] = new GridSize(4, 2),
            [SizePreset.Large] = new GridSize(6, 2),
        });

    public SettingsSchema Schema { get; } = new(
    [
        new ChoiceField("format", "Format de l'heure",
            [new SettingOption("24h", "24 h"), new SettingOption("12h", "12 h (AM/PM)")], "24h"),
        new ChoiceField("unit", "Unité", [new SettingOption("c", "°C"), new SettingOption("f", "°F")], "c"),
        new LookupField("location", "Ville de la météo")
        {
            Placeholder = "Cherche une ville",
            EmptyLabel = "Pas de météo",
        },
    ]);

    /// <summary>Jusqu'à 5 villes pour la requête ; la valeur est le lieu tel que les réglages le gardent.</summary>
    public async Task<IReadOnlyList<LookupResult>> LookupAsync(string fieldKey, string query, CancellationToken ct)
    {
        if (fieldKey != "location" || string.IsNullOrWhiteSpace(query)) return [];

        var cities = await weather.SearchCityAsync(query.Trim(), ct);
        return cities.Take(5).Select(city => new LookupResult(
            string.Join(", ", new[] { city.Name, city.Region, city.Country }.Where(part => !string.IsNullOrWhiteSpace(part))),
            JsonSerializer.SerializeToElement(new { name = city.Name, latitude = city.Latitude, longitude = city.Longitude })))
            .ToList();
    }

    public IModule Create(ModuleContext context) =>
        new ClockModule(Descriptor, ClockSettings.FromJson(context.Settings), weather, context.Time, context.Log);
}
