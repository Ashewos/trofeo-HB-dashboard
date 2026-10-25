using System.Globalization;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Trofeo.Core.Layout;
using Trofeo.Core.Rendering;
using Trofeo.ModuleKit;
using Trofeo.Modules.Agents;

namespace Trofeo.AgentPreview;

/// <summary>
/// Outil d'aperçu du module Claude Code / Codex. <c>--codex-probe [codexHome]</c> décrit la structure de la transcription
/// Codex la plus récente (les clés et leur genre, jamais les valeurs) ; <c>--mascot-sheet &lt;dossier&gt;</c> écrit une feuille
/// d'aperçu PNG par clip de chaque mascotte ; <c>--render &lt;png&gt;</c> dessine le module sur des données d'exemple.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage : Trofeo.AgentPreview <option>
          --codex-probe [codexHome]   décrit la structure (clés, sans valeurs) de la transcription Codex la plus récente
          --mascot-sheet <dossier>    écrit un PNG par clip de chaque mascotte (poses côte à côte, pixel de 8)
          --render <png> [--agent claude|codex] [--view feed|overview] [--size 6x3|8x4|10x4] [--text small|normal|large]
                                [--fade on|off] [--newest top|bottom]
                                      dessine le module (1920 × 462, en haut à gauche) sur des données d'exemple
                                      (--fade : fondu des lignes anciennes du fil, off par défaut ; --newest : ligne la plus récente en haut ou en bas)
        """;

    private static int Main(string[] args)
    {
        if (args.Length is 1 or 2 && args[0] == "--codex-probe")
        {
            return CodexProbeCommand(args.Length == 2 ? args[1] : CodexHome.Resolve());
        }

        if (args.Length == 2 && args[0] == "--mascot-sheet")
        {
            return MascotSheetCommand(args[1]);
        }

        if (args.Length >= 2 && args[0] == "--render")
        {
            return RenderCommand(args);
        }

        Console.WriteLine(Usage);
        return args.Length == 0 ? 0 : 2;
    }

    // Mise en page des feuilles : pixel de sprite de 8, au plus 6 poses par ligne.
    private const int SheetPixel = 8;
    private const int SheetColumns = 6;
    private const int SheetGap = 12;

    private static int MascotSheetCommand(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var definition in new[] { ClaudeMascot.Definition, CodexMascot.Definition })
            {
                var name = Slug(definition.Name);
                WriteSheet(Path.Combine(folder, name + "-repos.png"), definition, [definition.Rest, definition.Sleep]);
                foreach (var (state, clips) in definition.Clips.OrderBy(pair => pair.Key))
                {
                    foreach (var clip in clips)
                    {
                        var file = $"{name}-{Slug(StateName(state))}-{Slug(clip.Name)}.png";
                        WriteSheet(Path.Combine(folder, file), definition, clip.Poses);
                    }
                }
            }

            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Impossible d'écrire les feuilles dans " + folder + " : " + e.Message);
            return 1;
        }
    }

    private static string StateName(MascotState state) => state switch
    {
        MascotState.Idle => "inactif",
        MascotState.Working => "travaille",
        MascotState.Waiting => "attend",
        _ => "termine",
    };

    /// <summary>Minuscules, sans accent ni signe : « saut-drapeau » reste tel quel, « cœurs » devient « coeurs ».</summary>
    private static string Slug(string text)
    {
        var decomposed = text.ToLowerInvariant().Replace("œ", "oe", StringComparison.Ordinal).Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            slug.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }

        return slug.ToString();
    }

    /// <summary>
    /// Une feuille : les poses de gauche à droite, à la ligne après 6. Un calque qui bouge pendant la pose est dessiné à
    /// son départ en plein et, s'il arrive ailleurs, à son arrivée en pâle (sauf les pixels isolés : particules, étincelles).
    /// </summary>
    private static void WriteSheet(string path, MascotDefinition definition, IReadOnlyList<Pose> poses)
    {
        var cellWidth = definition.SceneWidth * SheetPixel;
        var cellHeight = definition.SceneHeight * SheetPixel;
        var columns = Math.Min(SheetColumns, poses.Count);
        var rows = (poses.Count + SheetColumns - 1) / SheetColumns;
        var theme = Themes.Classique;

        using var bitmap = new SKBitmap(
            SheetGap + columns * (cellWidth + SheetGap),
            SheetGap + rows * (cellHeight + SheetGap));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(theme.Background);
            using var cellPaint = new SKPaint { Color = theme.Panel, Style = SKPaintStyle.Fill };
            for (var i = 0; i < poses.Count; i++)
            {
                var x = SheetGap + i % SheetColumns * (cellWidth + SheetGap);
                var y = SheetGap + i / SheetColumns * (cellHeight + SheetGap);
                canvas.DrawRect(x, y, cellWidth, cellHeight, cellPaint);
                foreach (var layer in poses[i].Layers)
                {
                    if (layer.To != layer.From && layer.Sprite.Width * layer.Sprite.Height > 1)
                    {
                        layer.Sprite.Draw(canvas, x + layer.To.X * SheetPixel, y + layer.To.Y * SheetPixel, SheetPixel, alpha: 90);
                    }

                    layer.Sprite.Draw(canvas, x + layer.From.X * SheetPixel, y + layer.From.Y * SheetPixel, SheetPixel);
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        data.SaveTo(output);
    }

    // --- Aperçu du module ---

    private static int RenderCommand(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 2; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Option incomplète ou inconnue : " + args[i]);
                return 2;
            }

            options[args[i]] = args[i + 1];
        }

        var agent = options.GetValueOrDefault("--agent", "claude");
        var view = options.GetValueOrDefault("--view", "feed");
        var size = options.GetValueOrDefault("--size", "8x4");
        var text = options.GetValueOrDefault("--text", "normal");
        var fade = options.GetValueOrDefault("--fade", "off");
        var newest = options.GetValueOrDefault("--newest", "top");
        var known = new[] { "--agent", "--view", "--size", "--text", "--fade", "--newest" };
        if (options.Keys.Any(k => !known.Contains(k))
            || agent is not ("claude" or "codex") || view is not ("feed" or "overview")
            || size is not ("6x3" or "8x4" or "10x4") || text is not ("small" or "normal" or "large")
            || fade is not ("on" or "off") || newest is not ("top" or "bottom"))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        try
        {
            RenderModule(args[1], agent == "codex", view == "overview", size, text, fade == "on", newest);
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Impossible d'écrire " + args[1] + " : " + e.Message);
            return 1;
        }
    }

    private static void RenderModule(string path, bool codex, bool overview, string size, string text, bool fade, string newest)
    {
        var columns = int.Parse(size.AsSpan(0, size.IndexOf('x')), CultureInfo.InvariantCulture);
        var rows = int.Parse(size.AsSpan(size.IndexOf('x') + 1), CultureInfo.InvariantCulture);
        var scale = (text switch { "small" => TextSize.Small, "large" => TextSize.Large, _ => TextSize.Normal }).Factor();
        var theme = Themes.Classique;
        var now = DateTimeOffset.Now;

        IModuleFactory factory = codex
            ? new CodexFactory(_ => new SampleSource(now, codex: true))
            : new ClaudeCodeFactory(_ => new SampleSource(now, codex: false));
        using var json = JsonDocument.Parse(
            $$"""{"view":"{{(overview ? "overview" : "feed")}}","feedFade":{{(fade ? "true" : "false")}},"feedNewest":"{{newest}}"}""");
        var context = new ModuleContext(Guid.NewGuid(), json.RootElement.Clone(), TimeProvider.System, new ConsoleLog(), new NoSecrets());
        using var module = (AgentModule)factory.Create(context);
        module.PollNow();
        module.PollNow(); // le premier sondage choisit l'onglet, le second lit son détail

        var rect = GridGeometry.ModuleRect(0, 0, columns, rows);
        using var bitmap = new SKBitmap(GridGeometry.Width, GridGeometry.Height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(theme.Background);
            var bounds = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
            using var panel = new SKPaint { IsAntialias = true, Color = theme.Panel.WithAlpha((byte)Math.Round(theme.PanelOpacity * 255f)) };
            canvas.DrawRoundRect(bounds, theme.CornerRadius, theme.CornerRadius, panel);
            using var clip = new SKRoundRect(bounds, theme.CornerRadius);
            canvas.ClipRoundRect(clip, antialias: true);
            module.Render(canvas, new ModuleRenderContext(bounds, new GridSize(columns, rows), theme, scale, now));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        data.SaveTo(output);
        Console.WriteLine("Aperçu écrit : " + path);
    }

    private sealed class NoSecrets : ISecretStore
    {
        public string? Get(string key) => null;

        public void Set(string key, string? value)
        {
        }
    }

    private sealed class ConsoleLog : IModuleLog
    {
        public void Info(string message) => Console.WriteLine("Info : " + message);

        public void Warning(string message) => Console.WriteLine("Warning : " + message);

        public void Error(string message) => Console.Error.WriteLine("Erreur : " + message);
    }

    /// <summary>Quatre sessions dans les trois états, un fil avec un Edit +12 / −3, des tâches et des quotas.</summary>
    private sealed class SampleSource(DateTimeOffset now, bool codex) : IAgentSource
    {
        public AgentSnapshot Poll(string? selectedSessionId, DateTimeOffset at)
        {
            var sessions = new List<AgentSessionSummary>
            {
                new("s1", "trofeo-HB-dashboard", AgentActivity.Working, now.AddHours(-3), now.AddSeconds(-2)),
                new("s2", "site-perso", AgentActivity.Waiting, now.AddHours(-2), now.AddMinutes(-4)),
                new("s3", "docs", AgentActivity.Idle, now.AddHours(-1), now.AddMinutes(-22)),
                new("s4", "un-projet-au-nom-vraiment-trop-long", AgentActivity.Idle, now.AddMinutes(-30), now.AddHours(-1)),
            };

            var feed = new List<FeedEntry>
            {
                new("Read", "src/Trofeo.Modules.Agents/AgentModule.cs"),
                new("Grep", "\"IsAnimating\" in src"),
                new("Bash", "dotnet test tests/Trofeo.Modules.Agents.Tests"),
                new("Read", "tests/Trofeo.Modules.Agents.Tests/AgentModuleTests.cs"),
                new("Write", "src/Trofeo.Modules.Agents/AgentLayout.cs", 57, null),
                new("Edit", "src/Trofeo.Modules.Agents/AgentPainter.cs", 12, 3),
                new("Bash", "dotnet build tests/Trofeo.Modules.Agents.Tests"),
                new("Edit", "src/Trofeo.Modules.Agents/AgentModule.cs", 12, 3),
            };
            var tasks = codex
                ? new List<AgentTask>()
                : new List<AgentTask>
                {
                    new("Écrire la mise en page des onglets", AgentTaskState.Completed),
                    new("Dessiner le fil d'activité", AgentTaskState.Completed),
                    new("Dessiner la vue d'ensemble", AgentTaskState.InProgress),
                    new("Brancher les raccourcis clavier", AgentTaskState.Pending),
                    new("Vérifier sur le vrai écran", AgentTaskState.Pending),
                };
            var detail = new AgentSessionDetail(selectedSessionId ?? "s1", "Dessiner le module Claude Code / Codex", now.AddMinutes(-47),
                feed, tasks, codex ? "gpt-5.1-codex-max" : "claude-opus-5-5", 63);
            var quotas = new AgentQuotas(new QuotaWindow(42, now.AddHours(2.4)), new QuotaWindow(77.5, now.AddDays(3.2)), now);
            return new AgentSnapshot(true, sessions, detail, quotas);
        }

        public void Dispose()
        {
        }
    }

    private static int CodexProbeCommand(string codexHome)
    {
        var sessions = Path.Combine(codexHome, "sessions");
        FileInfo? latest = null;
        try
        {
            latest = Directory.Exists(sessions)
                ? new DirectoryInfo(sessions).EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories)
                    .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Impossible de parcourir " + sessions + " : " + e.Message);
            return 1;
        }

        if (latest is null)
        {
            Console.Error.WriteLine("Aucune transcription Codex trouvée dans " + sessions);
            return 1;
        }

        try
        {
            Console.WriteLine("Transcription : " + latest.Name);
            Console.WriteLine(CodexProbe.Describe(ReadLines(latest.FullName)));
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Impossible de lire " + latest.Name + " : " + e.Message);
            return 1;
        }
    }

    /// <summary>Les lignes du fichier, ouvert en partage complet : Codex peut être en train d'y écrire.</summary>
    private static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
