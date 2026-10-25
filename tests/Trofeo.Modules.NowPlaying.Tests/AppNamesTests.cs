namespace Trofeo.Modules.NowPlaying.Tests;

public class AppNamesTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Lecteur multimédia")]
    [InlineData("chrome.exe", "Chrome")]
    [InlineData("Firefox", "Firefox")]
    [InlineData("vlc.exe", "VLC")]
    [InlineData("DeezerMusic.Deezer_xyz!App", "Deezer")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", "Apple Music")]
    public void Friendly_KnownApps(string id, string expected) =>
        Assert.Equal(expected, AppNames.Friendly(id));

    [Fact]
    public void Friendly_Unknown_UsesExecutableName() =>
        Assert.Equal("Foobar2000", AppNames.Friendly("foobar2000.exe"));

    // « Contoso.Player_abc123!App » : avant « ! » → « Contoso.Player_abc123 », avant « _ » → « Contoso.Player »,
    // dernier segment après « . » → « Player ».
    [Theory]
    [InlineData("Contoso.Player_abc123!App", "Player")]
    [InlineData("Contoso.Player_abc123", "Player")]
    [InlineData("some.dotted.name.exe", "Name")]
    public void Friendly_Unknown_StripsPackageSuffixes(string id, string expected) =>
        Assert.Equal(expected, AppNames.Friendly(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Friendly_Empty_IsMedia(string? id) =>
        Assert.Equal("Média", AppNames.Friendly(id));
}
