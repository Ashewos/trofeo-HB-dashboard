using System.Text.Json;
using Trofeo.ModuleKit;
using Trofeo.Sensors;

namespace Trofeo.Modules.Monitoring;

/// <summary>Fabrique du module Monitoring PC, alimenté par la source de relevés partagée.</summary>
public sealed class MonitoringFactory(ISensorFeed feed) : IConfigurableModuleFactory
{
    public ModuleDescriptor Descriptor { get; } = new(
        "monitoring",
        "Monitoring PC",
        new GridSize(3, 2),
        new Dictionary<SizePreset, GridSize>
        {
            [SizePreset.Compact] = new GridSize(4, 2),
            [SizePreset.Ideal] = new GridSize(6, 2),
            [SizePreset.Large] = new GridSize(6, 4),
        });

    public SettingsSchema Schema { get; } = new(
    [
        new TextField("title", "Titre", MaxLength: 60) { Placeholder = "Nom du matériel détecté" },
        new ListField("rows", "Lignes affichées",
            [
                new SettingOption("cpu", "Processeur"),
                new SettingOption("gpu", "Carte graphique"),
                new SettingOption("ram", "Mémoire"),
                new SettingOption("vram", "Mémoire vidéo"),
                new SettingOption("clock", "Fréquences"),
                new SettingOption("power", "Consommation"),
                new SettingOption("fan", "Ventilateur"),
                new SettingOption("disk", "Disque"),
            ],
            MonitoringSettings.DefaultRows),
        new LookupField("gpu", "Carte graphique surveillée", Searchable: false)
        {
            EmptyLabel = "Automatique (celle qui a le plus de mémoire)",
        },
    ]);

    /// <summary>Les cartes graphiques du dernier relevé ; le libellé et la valeur sont le nom du matériel.</summary>
    public Task<IReadOnlyList<LookupResult>> LookupAsync(string fieldKey, string query, CancellationToken ct)
    {
        IReadOnlyList<LookupResult> results = fieldKey != "gpu" || feed.Latest?.Gpus is not { } gpus
            ? []
            : gpus.Select(g => new LookupResult(g.Name, JsonSerializer.SerializeToElement(g.Name))).ToList();
        return Task.FromResult(results);
    }

    public IModule Create(ModuleContext context) =>
        new MonitoringModule(Descriptor, MonitoringSettings.FromJson(context.Settings), feed, context.Time);
}
