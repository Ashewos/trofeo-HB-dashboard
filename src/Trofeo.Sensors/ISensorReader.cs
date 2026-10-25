namespace Trofeo.Sensors;

/// <summary>Source de relevés de capteurs.</summary>
public interface ISensorReader : IDisposable
{
    /// <summary>Lit les capteurs. Ne lève pas d'exception pour un capteur défaillant : la clé est simplement absente.</summary>
    SensorSnapshot Read();
}
