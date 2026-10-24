namespace Trofeo.Sensors;

/// <summary>Famille de matériel, indépendante de LibreHardwareMonitor.</summary>
public enum RawHardware { Cpu, GpuNvidia, GpuAmd, GpuIntel, Storage, Other }

/// <summary>Nature d'un capteur, indépendante de LibreHardwareMonitor.</summary>
public enum RawKind { Load, Temperature, Clock, Power, SmallData, Fan, Other }

/// <summary>Un capteur brut, tel que lu sur le matériel, avant le choix fait par <see cref="SensorMapper"/>.</summary>
/// <param name="Hardware">Famille du matériel porteur.</param>
/// <param name="HardwareName">Nom du matériel porteur (« Intel Core Ultra 7 265K »).</param>
/// <param name="Kind">Nature du capteur.</param>
/// <param name="Name">Nom du capteur (« CPU Package »).</param>
/// <param name="Value">Valeur lue ; <c>null</c> si le capteur n'en fournit pas.</param>
public sealed record RawSensor(RawHardware Hardware, string HardwareName, RawKind Kind, string Name, float? Value);
