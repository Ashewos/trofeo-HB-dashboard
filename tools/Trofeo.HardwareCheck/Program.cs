using System.Diagnostics;
using SkiaSharp;
using Trofeo.Core.Rendering;
using Trofeo.Device;

// Vérification sur le vrai écran. Chaque mode se termine seul ; fermer Thermalright TRCC avant.
const int Width = 1920;
const int Height = 462;

var rotation = 180;
string? mode = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--orientation" when i + 1 < args.Length && int.TryParse(args[i + 1], out var r) && r is 0 or 180:
            rotation = r;
            i++;
            mode ??= "orientation";
            break;
        case "--fps":
            mode = "fps";
            break;
        case "--recovery":
            mode = "recovery";
            break;
        default:
            Console.Error.WriteLine("Usage : Trofeo.HardwareCheck [--orientation <0|180>] [--fps] [--recovery]");
            return 2;
    }
}

mode ??= "orientation";
return mode switch
{
    "fps" => RunFps(rotation),
    "recovery" => RunRecovery(rotation),
    _ => RunOrientation(rotation),
};

static byte[] OrientationFrame(int rotation)
{
    using var bitmap = new SKBitmap(Width, Height);
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Clear(SKColor.Parse("#0E1116"));
        using var frame = new SKPaint { Color = SKColors.LimeGreen, Style = SKPaintStyle.Stroke, StrokeWidth = 8, IsAntialias = true };
        canvas.DrawRect(4, 4, Width - 8, Height - 8, frame);
        using var font = new SKFont(Themes.Classique.Typeface, 96);
        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("HAUT GAUCHE", 40, 40 + 96, font, text);
        using var small = new SKFont(Themes.Classique.Typeface, 48);
        canvas.DrawText($"Rotation {rotation}° - 1920 x 462", 40, Height - 40, small, text);
        canvas.DrawLine(0, 0, 160, 160, frame);
    }

    return JpegEncoder.Encode(bitmap, rotation);
}

static byte[] AnimatedFrame(int index, int rotation)
{
    using var bitmap = new SKBitmap(Width, Height);
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Clear(SKColor.Parse("#0E1116"));
        var x = index * 40 % (Width - 200);
        using var bar = new SKPaint { Color = SKColors.LimeGreen, IsAntialias = true };
        canvas.DrawRect(x, 100, 200, 262, bar);
        using var font = new SKFont(Themes.Classique.Typeface, 72);
        using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText($"Image {index}", 40, 90, font, text);
    }

    return JpegEncoder.Encode(bitmap, rotation);
}

static ScreenDevice Connect(out List<string> transitions)
{
    var device = new ScreenDevice(new WinUsbTransportFactory(), TimeProvider.System);
    var log = new List<string>();
    var clock = Stopwatch.StartNew();
    device.StateChanged += (_, _) => log.Add($"{clock.ElapsedMilliseconds,6} ms  {device.State}" + (device.Reason is null ? "" : $" ({device.Reason})"));
    transitions = log;
    return device;
}

static int RunOrientation(int rotation)
{
    using var device = Connect(out _);
    if (!device.TryConnect())
    {
        Console.WriteLine($"Connexion impossible : {device.Reason}");
        return 1;
    }

    var ok = device.TrySendFrame(OrientationFrame(rotation));
    Console.WriteLine($"Mire d'orientation envoyée (rotation {rotation}) : {ok}");
    return ok ? 0 : 1;
}

static int RunFps(int rotation)
{
    var frames = Enumerable.Range(0, 24).Select(i => AnimatedFrame(i, rotation)).ToArray();
    Console.WriteLine($"Images pré-rendues : {frames.Length}, taille moyenne {frames.Average(f => f.Length):F0} octets");

    using var device = Connect(out var transitions);
    if (!device.TryConnect())
    {
        Console.WriteLine($"Connexion impossible : {device.Reason}");
        return 1;
    }

    var sent = 0;
    var failed = 0;
    var missedAcks = 0;
    device.StateChanged += (_, _) =>
    {
        if (device.State == ScreenState.Recovering)
            missedAcks++;
    };

    var clock = Stopwatch.StartNew();
    while (clock.Elapsed < TimeSpan.FromSeconds(10))
    {
        if (device.TrySendFrame(frames[(sent + failed) % frames.Length]))
            sent++;
        else
            failed++;
    }

    var seconds = clock.Elapsed.TotalSeconds;
    Console.WriteLine($"images envoyées : {sent} en {seconds:F2} s");
    Console.WriteLine($"images/s : {sent / seconds:F2}");
    Console.WriteLine($"validations manquées : {missedAcks}");
    Console.WriteLine($"envois échoués : {failed}");
    foreach (var line in transitions)
        Console.WriteLine("  " + line);
    return 0;
}

static int RunRecovery(int rotation)
{
    var pattern = OrientationFrame(rotation);

    // 1. Provoquer le gel : deux images d'affilée sans lire la validation, comme pendant l'essai du 24/10.
    Console.WriteLine("Provocation du gel (deux images sans lire la validation)...");
    var factory = new WinUsbTransportFactory();
    IUsbTransport? raw;
    try
    {
        raw = factory.TryOpen();
    }
    catch (UsbBusyException)
    {
        Console.WriteLine("Écran utilisé par un autre programme (fermez TRCC).");
        return 1;
    }

    if (raw is null)
    {
        Console.WriteLine("Écran non branché.");
        return 1;
    }

    using (raw)
    {
        raw.Write(LyProtocol.BuildHandshake(), 1000);
        var reply = new byte[512];
        var read = raw.Read(reply, 1000);
        Console.WriteLine($"poignée de main valide : {LyProtocol.IsValidHandshakeReply(reply.AsSpan(0, read))}");

        var packets = LyProtocol.Packetize(pattern);
        for (var frame = 0; frame < 2; frame++)
        {
            try
            {
                for (var offset = 0; offset < packets.Length; offset += 4096)
                    raw.Write(packets.AsSpan(offset, Math.Min(4096, packets.Length - offset)), 5000);
                Console.WriteLine($"image {frame + 1} écrite sans lire la validation");
            }
            catch (UsbTimeoutException)
            {
                Console.WriteLine($"image {frame + 1} : délai d'écriture dépassé (gel provoqué)");
                break;
            }
        }
    }

    // 2. Reprise par ScreenDevice.
    using var device = Connect(out var transitions);
    var deadline = Stopwatch.StartNew();
    var connected = device.TryConnect();
    while (!connected && deadline.Elapsed < TimeSpan.FromSeconds(5))
    {
        Thread.Sleep(250);
        connected = device.TryConnect();
    }

    var recovered = connected && device.TrySendFrame(pattern);
    Console.WriteLine("transitions d'état :");
    foreach (var line in transitions)
        Console.WriteLine("  " + line);
    Console.WriteLine($"recovered: {recovered}");
    return recovered ? 0 : 1;
}
