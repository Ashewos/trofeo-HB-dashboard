using System.Globalization;
using System.Text;
using SkiaSharp;
using Trofeo.Core.Rendering;
using Trofeo.Modules.Agents;

namespace Trofeo.AgentPreview;

/// <summary>
/// Outil d'aperçu du module Claude Code / Codex. <c>--codex-probe [codexHome]</c> décrit la structure de la transcription
/// Codex la plus récente (les clés et leur genre, jamais les valeurs) ; <c>--mascot-sheet &lt;dossier&gt;</c> écrit une feuille
/// d'aperçu PNG par clip de chaque mascotte ; les autres options viennent avec le dessin du module.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage : Trofeo.AgentPreview <option>
          --codex-probe [codexHome]   décrit la structure (clés, sans valeurs) de la transcription Codex la plus récente
          --mascot-sheet <dossier>    écrit un PNG par clip de chaque mascotte (poses côte à côte, pixel de 8)
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
