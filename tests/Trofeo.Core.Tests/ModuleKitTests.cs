using Trofeo.ModuleKit;

namespace Trofeo.Core.Tests;

public class ModuleKitTests
{
    [Fact]
    public void TextSize_Factors()
    {
        Assert.Equal(0.85f, TextSize.Small.Factor());
        Assert.Equal(1.0f, TextSize.Normal.Factor());
        Assert.Equal(1.2f, TextSize.Large.Factor());
    }

    [Fact]
    public void GridSize_Equality()
    {
        Assert.True(new GridSize(4, 2) == new GridSize(4, 2));
    }
}
