using System.Reflection;
using SkiaSharp;
using Trofeo.Core.Rendering;

namespace Trofeo.Core.Tests;

public class FontsTests
{
    private const string Prefix = "Trofeo.Core.Assets.Fonts.";

    /// <summary>Faux gestionnaire de polices du système : connaît les familles données, note les appels.</summary>
    private sealed class FakeSystemFonts : ISystemFonts
    {
        private readonly Dictionary<string, SKTypeface> _known;
        public List<string> Asked { get; } = new();

        public FakeSystemFonts(params (string Name, SKTypeface Face)[] known) =>
            _known = known.ToDictionary(k => k.Name, k => k.Face, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Families => _known.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

        public SKTypeface? Match(string family)
        {
            Asked.Add(family);
            return _known.TryGetValue(family, out var face) ? face : null;
        }
    }

    [Fact]
    public void Embedded_AllFamiliesLoad()
    {
        Assert.Equal(
            new[] { "Barlow Semi Condensed", "Manrope", "Pixelify Sans", "VT323" },
            EmbeddedFonts.Families);

        foreach (var name in EmbeddedFonts.Families)
        {
            var face = EmbeddedFonts.Find(name);
            Assert.NotNull(face);
            // DirectWrite (Windows) nomme « Barlow » la famille « Barlow Semi Condensed » : voir IsSameFamily.
            Assert.True(SkiaSystemFonts.IsSameFamily(name, face!.FamilyName), $"{name} ≠ {face.FamilyName}");
        }

        Assert.Equal(500, EmbeddedFonts.Find(EmbeddedFonts.Manrope)!.FontWeight);
    }

    [Theory]
    [InlineData("Barlow Semi Condensed", "Barlow Semi Condensed", true)]
    [InlineData("Barlow Semi Condensed", "Barlow", true)]
    [InlineData("arial narrow", "Arial", true)]
    [InlineData("Barlowe", "Barlow", false)]
    [InlineData("Police Inconnue", "Segoe UI", false)]
    [InlineData("Manrope", "", false)]
    public void IsSameFamily_AcceptsDirectWriteNames(string requested, string actual, bool expected) =>
        Assert.Equal(expected, SkiaSystemFonts.IsSameFamily(requested, actual));

    [Fact]
    public void Embedded_Find_IgnoresCase()
    {
        Assert.NotNull(EmbeddedFonts.Find("pixelify sans"));
        Assert.Null(EmbeddedFonts.Find("Arial"));
    }

    [Fact]
    public void Embedded_Find_KeepsTheSameInstance()
    {
        Assert.Same(EmbeddedFonts.Find("Manrope"), EmbeddedFonts.Find("MANROPE"));
    }

    [Fact]
    public void Resolve_Embedded_BeatsSystem()
    {
        var system = new FakeSystemFonts(("Manrope", SKTypeface.Default));
        var fallback = SKTypeface.Default;

        var face = FontResolver.Resolve("Manrope", fallback, system);

        Assert.Same(EmbeddedFonts.Find("Manrope"), face);
        Assert.Empty(system.Asked);
    }

    [Fact]
    public void Resolve_SystemFont_IsUsed()
    {
        var segoe = SKTypeface.Default;
        var system = new FakeSystemFonts(("Segoe UI", segoe));
        var fallback = EmbeddedFonts.Find("Barlow Semi Condensed")!;
        var warnings = new List<string>();

        var face = FontResolver.Resolve("Segoe UI", fallback, system, warnings.Add);

        Assert.Same(segoe, face);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Resolve_UnknownFamily_FallsBackToTheme()
    {
        var system = new FakeSystemFonts();
        var fallback = EmbeddedFonts.Find("Barlow Semi Condensed")!;
        var warnings = new List<string>();

        var face = FontResolver.Resolve("Zzz Inconnue", fallback, system, warnings.Add);

        Assert.Same(fallback, face);
        Assert.Equal(new[] { "Police « Zzz Inconnue » introuvable : police du thème utilisée." }, warnings);
    }

    [Fact]
    public void Resolve_UnknownFamily_WithoutWarnCallback_DoesNotThrow()
    {
        var fallback = EmbeddedFonts.Find("VT323")!;

        Assert.Same(fallback, FontResolver.Resolve("Zzz Inconnue", fallback, new FakeSystemFonts()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_Blank_IsFallbackWithoutWarning(string? family)
    {
        var system = new FakeSystemFonts();
        var fallback = EmbeddedFonts.Find("Barlow Semi Condensed")!;
        var warnings = new List<string>();

        var face = FontResolver.Resolve(family, fallback, system, warnings.Add);

        Assert.Same(fallback, face);
        Assert.Empty(warnings);
        Assert.Empty(system.Asked);
    }

    [Fact]
    public void SkiaSystemFonts_DoesNotAssumeAnyInstalledFont()
    {
        var system = new SkiaSystemFonts();

        Assert.NotNull(system.Families);
        Assert.Null(system.Match("ZzzNoSuchFont"));
        Assert.Equal(system.Families.Count, system.Families.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Licenses_ShipWithEachFont()
    {
        var fontFiles = typeof(FontLoader).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".ttf", StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..])
            .ToList();
        Assert.Equal(5, fontFiles.Count);

        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        foreach (var file in fontFiles)
        {
            var family = file[..file.IndexOf('-')];
            Assert.True(File.Exists(Path.Combine(folder, $"OFL-{family}.txt")), $"OFL-{family}.txt manque pour {file}");
        }
    }
}
