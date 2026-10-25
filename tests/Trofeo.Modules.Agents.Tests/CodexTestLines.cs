using System.Text.Json;

namespace Trofeo.Modules.Agents.Tests;

/// <summary>Lignes de transcription Codex écrites à la main, au format attendu (communautaire).</summary>
internal static class CodexTestLines
{
    public static string At(DateTimeOffset moment) => moment.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    public static string Meta(string id, string cwd, DateTimeOffset at) =>
        Line("session_meta", at, "{\"id\":" + JsonSerializer.Serialize(id) + ",\"timestamp\":\"" + At(at) + "\",\"cwd\":" + JsonSerializer.Serialize(cwd) + "}");

    /// <summary>Un <c>event_msg</c> ; <paramref name="extra"/> est un fragment JSON ajouté après le type (« ,"message":"…" »).</summary>
    public static string Event(string type, DateTimeOffset at, string extra = "") =>
        Line("event_msg", at, "{\"type\":\"" + type + "\"" + extra + "}");

    public static string Item(string payloadJson, DateTimeOffset at) => Line("response_item", at, payloadJson);

    public static string Call(string name, string arguments, DateTimeOffset at) =>
        Item("{\"type\":\"function_call\",\"name\":\"" + name + "\",\"arguments\":" + JsonSerializer.Serialize(arguments) + ",\"call_id\":\"c1\"}", at);

    public static string Line(string type, DateTimeOffset at, string payloadJson) =>
        "{\"timestamp\":\"" + At(at) + "\",\"type\":\"" + type + "\",\"payload\":" + payloadJson + "}";
}
