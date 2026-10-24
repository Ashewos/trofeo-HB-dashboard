using System.Text.Json;
using Trofeo.ModuleKit;

namespace Trofeo.Core.Layout;

/// <summary>Instance d'un module posée sur le damier, avec ses réglages propres.</summary>
public sealed record ModulePlacement(
    Guid InstanceId,
    string TypeId,
    int Column,
    int Row,
    int Columns,
    int Rows,
    TextSize TextSize,
    JsonElement Settings);
