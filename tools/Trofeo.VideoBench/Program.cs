using System.Diagnostics;
using System.Globalization;
using Trofeo.Display.Video;

// Banc de mesure du fond vidéo, sans écran ni service : ouvre un fichier avec MediaFoundationBackground et appelle
// Advance à la cadence du service (12 fois par seconde, temps monotone), en lisant Current à chaque appel comme le fait le rendu.
// Mesure le processus lui-même : mémoire propre (min, max) et temps processeur. Une variante par lancement : la mémoire
// d'un processus ne redescend pas toujours, on ne mélange pas les cas.
//
//   dotnet run -c Debug --project tools/Trofeo.VideoBench -- <fichier> [--seconds 60] [--warmup 10] [--threads N | --decoder-default]
//                                                                    [--fast] [--label texte]
//   --threads N : nombre de fils du décodeur (défaut du service : 1) ; --decoder-default : le réglage du décodeur lui-même.
//   --fast : débit maximal (sans attendre 1/12 s entre deux appels), pour mesurer la marge sur le temps réel.

var options = Options.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Usage : Trofeo.VideoBench <fichier> [--seconds 60] [--warmup 10] [--threads N | --decoder-default] [--fast] [--label texte]");
    return 2;
}

var tuning = options.DecoderDefault ? new DecoderTuning(null) : options.Threads is { } threads ? new DecoderTuning(threads) : DecoderTuning.Default;
string? warning = null;
using var background = MediaFoundationBackground.TryOpen(options.Path, message => warning = message, tuning);
if (background is null)
{
    Console.Error.WriteLine($"Ouverture impossible : {warning}");
    return 1;
}
if (warning is not null) Console.Error.WriteLine($"Avertissement : {warning}");

var process = Process.GetCurrentProcess();
var clock = Stopwatch.StartNew();
var period = TimeSpan.FromSeconds(1.0 / 12);
var next = period;
var warmup = TimeSpan.FromSeconds(options.Warmup);
var end = warmup + TimeSpan.FromSeconds(options.Seconds);
double simulated = 0;                       // mode --fast : le temps avance de 1/12 s par appel, sans attendre

TimeSpan cpuStart = default, wallStart = default;
int samplesStart = 0, calls = 0, changes = 0;
long privateMin = long.MaxValue, privateMax = 0, workingMin = long.MaxValue, workingMax = 0;
var worst = TimeSpan.Zero;
var durations = new List<double>();
var nextSample = TimeSpan.Zero;
bool measuring = false;
var wallNow = TimeSpan.Zero;

while (true)
{
    if (options.Fast)
    {
        simulated += 1.0 / 12;
        wallNow = clock.Elapsed;
    }
    else
    {
        wallNow = clock.Elapsed;
        if (wallNow < next) { Thread.Sleep(Math.Max(1, (int)(next - wallNow).TotalMilliseconds)); continue; }
        next += period;
    }

    var elapsed = options.Fast ? TimeSpan.FromSeconds(simulated) : wallNow;
    var t0 = Stopwatch.GetTimestamp();
    bool changed = background.Advance(elapsed);
    var image = background.Current;
    _ = image.Width;
    var took = Stopwatch.GetElapsedTime(t0);

    if (!measuring && wallNow >= warmup)
    {
        measuring = true;
        process.Refresh();
        cpuStart = process.TotalProcessorTime;
        wallStart = wallNow;
        samplesStart = background.SamplesRead;
    }

    if (measuring)
    {
        calls++;
        if (changed) changes++;
        if (took > worst) worst = took;
        durations.Add(took.TotalMilliseconds);
    }

    if (wallNow >= nextSample)
    {
        nextSample = wallNow + TimeSpan.FromMilliseconds(250);
        process.Refresh();
        if (measuring)
        {
            privateMin = Math.Min(privateMin, process.PrivateMemorySize64);
            privateMax = Math.Max(privateMax, process.PrivateMemorySize64);
            workingMin = Math.Min(workingMin, process.WorkingSet64);
            workingMax = Math.Max(workingMax, process.WorkingSet64);
        }
    }

    if (wallNow >= end) break;
}

process.Refresh();
var cpu = process.TotalProcessorTime - cpuStart;
var wall = wallNow - wallStart;
int samples = background.SamplesRead - samplesStart;
durations.Sort();
double Percentile(double p) => durations.Count == 0 ? 0 : durations[Math.Min(durations.Count - 1, (int)(durations.Count * p))];
double coreShare = cpu.TotalSeconds / wall.TotalSeconds * 100;
var invariant = CultureInfo.InvariantCulture;
string Mb(long bytes) => (bytes / 1048576.0).ToString("F0", invariant);

Console.WriteLine(string.Join(" | ",
    $"{options.Label}",
    $"propre {Mb(privateMin)}-{Mb(privateMax)} Mo",
    $"vive {Mb(workingMin)}-{Mb(workingMax)} Mo",
    $"cpu {coreShare.ToString("F1", invariant)} % d'un coeur = {(coreShare / Environment.ProcessorCount).ToString("F2", invariant)} % machine",
    $"{samples} echantillons en {wall.TotalSeconds.ToString("F1", invariant)} s ({(samples / wall.TotalSeconds).ToString("F1", invariant)} i/s)",
    $"{calls} appels, {changes} changements",
    $"Advance moy {(durations.Count == 0 ? 0 : durations.Average()).ToString("F1", invariant)} ms, p99 {Percentile(0.99).ToString("F1", invariant)} ms, max {worst.TotalMilliseconds.ToString("F0", invariant)} ms"));
return 0;

internal sealed record Options(string Path, double Seconds, double Warmup, int? Threads, bool DecoderDefault, bool Fast, string Label)
{
    public static Options? Parse(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--")) return null;
        string path = args[0];
        double seconds = 60, warmup = 10;
        int? threads = null;
        bool decoderDefault = false, fast = false;
        string? label = null;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seconds" when i + 1 < args.Length: seconds = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--warmup" when i + 1 < args.Length: warmup = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--threads" when i + 1 < args.Length: threads = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--decoder-default": decoderDefault = true; break;
                case "--fast": fast = true; break;
                case "--label" when i + 1 < args.Length: label = args[++i]; break;
                default: return null;
            }
        }
        return new Options(path, seconds, warmup, threads, decoderDefault, fast, label ?? System.IO.Path.GetFileName(path));
    }
}
