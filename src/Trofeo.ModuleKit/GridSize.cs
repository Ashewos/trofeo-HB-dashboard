namespace Trofeo.ModuleKit;

/// <summary>Taille en cellules du damier (colonnes × lignes).</summary>
public readonly record struct GridSize(int Columns, int Rows);

/// <summary>Préréglages de taille proposés pour un module.</summary>
public enum SizePreset { Compact, Ideal, Large }
