using System.Text;
using System.Xml.Linq;

namespace Trofeo.Sensors.Tests;

public sealed class HelperInstallerTests : IDisposable
{
    private const string Task = "\"" + TaskDefinition.TaskName + "\"";
    private const string PawnIoSetup = @"C:\dl\PawnIO_setup.exe";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "trofeo-installer-" + Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly string _install;
    private readonly FakeRunner _runner = new();
    private readonly FakePawnIo _pawnIo = new();

    public HelperInstallerTests()
    {
        _source = Path.Combine(_root, "source");
        _install = Path.Combine(_root, "install") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_source);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private HelperInstaller Installer() => new(_runner, _pawnIo, _source, _install);

    [Fact]
    public void Install_PawnIoMissing_NoPath_ChangesNothing()
    {
        File.WriteAllText(Path.Combine(_source, "Trofeo.SensorHelper.exe"), "exe");

        var result = Installer().Install(null);

        Assert.Equal(InstallResult.PawnIoMissing, result);
        Assert.Empty(_runner.Calls);
        Assert.False(Directory.Exists(_install));
    }

    [Fact]
    public void Install_RunsPawnIoSetupThenTask()
    {
        var result = Installer().Install(PawnIoSetup);

        Assert.Equal(InstallResult.Installed, result);
        Assert.Equal(
        [
            (PawnIoSetup, "-install -silent"),
            ("schtasks", $"/End /TN {Task}"),
            ("schtasks", $"/Create /TN {Task} /XML \"{_runner.XmlPath}\" /F"),
            ("schtasks", $"/Run /TN {Task}"),
        ], _runner.Calls);
    }

    [Fact]
    public void Install_PawnIoAlreadyInstalled_SkipsSetup()
    {
        _pawnIo.Installed = true;

        var result = Installer().Install(PawnIoSetup);

        Assert.Equal(InstallResult.Installed, result);
        Assert.DoesNotContain(_runner.Calls, c => c.File == PawnIoSetup);
        Assert.Equal(3, _runner.Calls.Count);
    }

    [Fact]
    public void Install_PawnIoAlreadyInstalled_NoPath_Installs()
    {
        _pawnIo.Installed = true;
        Assert.Equal(InstallResult.Installed, Installer().Install(null));
    }

    [Fact]
    public void Install_PawnIoReboot_ReturnsRebootRequired()
    {
        _runner.ExitCodes[PawnIoSetup] = 3010;

        var result = Installer().Install(PawnIoSetup);

        Assert.Equal(InstallResult.InstalledRebootRequired, result);
        Assert.Contains(_runner.Calls, c => c.Args.StartsWith("/Run", StringComparison.Ordinal)); // la suite a bien lieu
    }

    [Fact]
    public void Install_PawnIoFails_StopsThere()
    {
        _runner.ExitCodes[PawnIoSetup] = 1603;
        File.WriteAllText(Path.Combine(_source, "a.txt"), "a");

        var result = Installer().Install(PawnIoSetup);

        Assert.Equal(InstallResult.PawnIoFailed, result);
        Assert.Single(_runner.Calls);
        Assert.False(Directory.Exists(_install));
    }

    [Fact]
    public void Install_IgnoresEndExitCode_ButCreateFailureThrows()
    {
        _pawnIo.Installed = true;
        _runner.ExitCodes["schtasks /End"] = 1; // tâche absente : sans conséquence
        Assert.Equal(InstallResult.Installed, Installer().Install(null));

        _runner.ExitCodes["schtasks /Create"] = 1;
        Assert.Throws<InvalidOperationException>(() => Installer().Install(null));
    }

    [Fact]
    public void Install_CopiesFilesRecursively_Overwriting()
    {
        _pawnIo.Installed = true;
        Directory.CreateDirectory(Path.Combine(_source, "sub", "deep"));
        File.WriteAllText(Path.Combine(_source, "Trofeo.SensorHelper.exe"), "nouveau");
        File.WriteAllText(Path.Combine(_source, "sub", "deep", "lib.dll"), "lib");
        Directory.CreateDirectory(_install);
        File.WriteAllText(Path.Combine(_install, "Trofeo.SensorHelper.exe"), "ancien");
        File.WriteAllText(Path.Combine(_install, "autre.txt"), "gardé");

        Installer().Install(null);

        Assert.Equal("nouveau", File.ReadAllText(Path.Combine(_install, "Trofeo.SensorHelper.exe")));
        Assert.Equal("lib", File.ReadAllText(Path.Combine(_install, "sub", "deep", "lib.dll")));
        Assert.Equal("gardé", File.ReadAllText(Path.Combine(_install, "autre.txt"))); // rien n'est supprimé avant la copie
    }

    [Fact]
    public void Install_FromTheInstalledCopy_DoesNotCopy()
    {
        _pawnIo.Installed = true;
        File.WriteAllText(Path.Combine(_source, "a.txt"), "a");

        var installer = new HelperInstaller(_runner, _pawnIo, _source + Path.DirectorySeparatorChar, _source);
        Assert.Equal(InstallResult.Installed, installer.Install(null));

        Assert.Equal("a", File.ReadAllText(Path.Combine(_source, "a.txt")));
        Assert.Equal(3, _runner.Calls.Count);
    }

    [Fact]
    public void Install_TaskXml_TargetsInstalledExe_AndTempFileIsRemoved()
    {
        _pawnIo.Installed = true;

        Installer().Install(null);

        var xml = XDocument.Parse(_runner.XmlAtCreate!);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var command = xml.Root!.Element(ns + "Actions")!.Element(ns + "Exec")!.Element(ns + "Command")!.Value;
        Assert.Equal(Path.Combine(_install, "Trofeo.SensorHelper.exe"), command);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, _runner.XmlBytesAtCreate![..2]); // UTF-16 LE avec BOM
        Assert.False(File.Exists(_runner.XmlPath));
    }

    [Fact]
    public void Uninstall_DeletesTaskAndFolder()
    {
        Directory.CreateDirectory(Path.Combine(_install, "sub"));
        File.WriteAllText(Path.Combine(_install, "sub", "a.txt"), "a");

        Installer().Uninstall();

        Assert.Equal(
        [
            ("schtasks", $"/End /TN {Task}"),
            ("schtasks", $"/Delete /TN {Task} /F"),
        ], _runner.Calls);
        Assert.False(Directory.Exists(_install));
    }

    [Fact]
    public void Uninstall_NothingInstalled_DoesNotThrow()
    {
        _runner.ExitCodes["schtasks"] = 1;
        Installer().Uninstall();
        Assert.Equal(2, _runner.Calls.Count);
    }

    private sealed class FakePawnIo : IPawnIoDetector
    {
        public bool Installed { get; set; }

        public bool IsInstalled() => Installed;
    }

    private sealed class FakeRunner : ICommandRunner
    {
        public List<(string File, string Args)> Calls { get; } = [];

        /// <summary>Code de sortie par fichier, ou par « fichier /Verbe » ; 0 par défaut.</summary>
        public Dictionary<string, int> ExitCodes { get; } = [];

        public string? XmlPath { get; private set; }

        public string? XmlAtCreate { get; private set; }

        public byte[]? XmlBytesAtCreate { get; private set; }

        public int Run(string fileName, string arguments)
        {
            Calls.Add((fileName, arguments));
            if (arguments.StartsWith("/Create", StringComparison.Ordinal))
            {
                var start = arguments.IndexOf("/XML \"", StringComparison.Ordinal) + 6;
                XmlPath = arguments[start..arguments.IndexOf('"', start)];
                XmlBytesAtCreate = File.ReadAllBytes(XmlPath);
                XmlAtCreate = File.ReadAllText(XmlPath, Encoding.Unicode);
            }

            var verb = arguments.Split(' ')[0];
            if (ExitCodes.TryGetValue($"{fileName} {verb}", out var byVerb)) return byVerb;
            return ExitCodes.GetValueOrDefault(fileName);
        }
    }
}
