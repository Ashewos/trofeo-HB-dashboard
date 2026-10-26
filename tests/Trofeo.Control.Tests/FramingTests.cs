using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Trofeo.ModuleKit;

namespace Trofeo.Control.Tests;

public class FramingTests
{
    [Fact]
    public async Task Framing_RoundTrip_JsonAndImage()
    {
        var stream = new MemoryStream();
        var json = Encoding.UTF8.GetBytes("{\"id\":1,\"type\":\"status\"}");
        var jpeg = new byte[] { 0xFF, 0xD8, 1, 2, 3, 0xFF, 0xD9 };

        await Framing.WriteJsonAsync(stream, json, CancellationToken.None);
        await Framing.WriteImageAsync(stream, 180, jpeg, CancellationToken.None);
        stream.Position = 0;

        var first = await Framing.ReadAsync(stream, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(FrameKind.Json, first.Value.Kind);
        Assert.Equal(json, first.Value.Payload);

        var second = await Framing.ReadAsync(stream, CancellationToken.None);
        Assert.NotNull(second);
        Assert.Equal(FrameKind.Image, second.Value.Kind);
        var (rotation, bytes) = Framing.ParseImage(second.Value.Payload);
        Assert.Equal(180, rotation);
        Assert.Equal(jpeg, bytes);

        // Fin de flux propre : plus de trame.
        Assert.Null(await Framing.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Framing_UsesLittleEndianLengthThenKind()
    {
        var stream = new MemoryStream();
        await Framing.WriteJsonAsync(stream, new byte[] { 0x7B, 0x7D }, CancellationToken.None);

        var raw = stream.ToArray();
        // La longueur compte l'octet de sorte et la charge.
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(raw));
        Assert.Equal(1, raw[4]);
        Assert.Equal(new byte[] { 0x7B, 0x7D }, raw[5..]);
    }

    [Fact]
    public async Task Framing_Oversize_IsRejected()
    {
        // En lecture : une longueur annoncée au-delà de 8 Mo.
        var header = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(header, Framing.MaxFrameBytes + 1);
        header[4] = 1;
        var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadAsync(stream, CancellationToken.None));

        // En écriture : une charge trop grosse n'est pas envoyée.
        var output = new MemoryStream();
        var tooBig = new byte[Framing.MaxFrameBytes];
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.WriteImageAsync(output, 0, tooBig, CancellationToken.None));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task Framing_TruncatedFrame_Throws()
    {
        var header = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(header, 100);
        header[4] = 1;
        var stream = new MemoryStream([.. header, 1, 2, 3]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => Framing.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public void Serializer_ReadsSchemaWhoseKindIsNotFirst()
    {
        const string json = """{"key":"mode","label":"Mode","options":[{"value":"a","label":"A"}],"default":"a","kind":"choice"}""";

        var field = JsonSerializer.Deserialize<SettingField>(json, ControlJson.Options);

        var choice = Assert.IsType<ChoiceField>(field);
        Assert.Equal("mode", choice.Key);
        Assert.Equal("a", choice.Default);
    }

    [Fact]
    public void Serializer_WritesEnumsAsStrings()
    {
        var json = JsonSerializer.Serialize(new ModuleState(Guid.NewGuid(), "clock", ModuleHealth.Offline, null, []), ControlJson.Options);
        Assert.Contains("\"Offline\"", json);
    }
}
