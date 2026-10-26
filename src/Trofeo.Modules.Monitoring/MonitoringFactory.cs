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
    ]);

    public IModule Create(ModuleContext context) =>
        new MonitoringModule(Descriptor, MonitoringSettings.FromJson(context.Settings), feed, context.Time);
}
