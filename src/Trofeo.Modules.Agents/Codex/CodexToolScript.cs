using System.Globalization;
using System.Text;

namespace Trofeo.Modules.Agents;

internal sealed record CodexScriptCall(string Name, string? Text, string? Shell);

/// <summary>
/// Lit les appels tools.nom(...) dans un script d'orchestration exec, sans jamais exécuter de JavaScript.
/// Seuls les arguments littéraux sont affichés : une expression dynamique n'est pas évaluée ni devinée.
/// Les chaînes et commentaires sont opaques, donc du code cité dans un patch n'est pas pris pour un appel réel.
/// </summary>
internal static class CodexToolScript
{
    private sealed record Token(string Value, bool Literal = false, bool Known = true);

    public static IEnumerable<CodexScriptCall> Read(string script)
    {
        var tokens = Tokenize(script);
        for (var i = 0; i + 4 < tokens.Count; i++)
        {
            if (tokens[i].Literal || tokens[i].Value != "tools" || tokens[i + 1].Value != "."
                || tokens[i + 2].Literal || tokens[i + 3].Value != "(")
            {
                continue;
            }

            var name = tokens[i + 2].Value;
            var start = i + 4;
            if (name == "apply_patch")
            {
                yield return new(name, tokens[start].Literal && tokens[start].Known && start + 1 < tokens.Count && tokens[start + 1].Value == ")"
                    ? tokens[start].Value : null, null);
            }
            else
            {
                var fields = Fields(tokens, start);
                var text = fields.GetValueOrDefault("cmd") ?? fields.GetValueOrDefault("command")
                    ?? fields.GetValueOrDefault("description") ?? fields.GetValueOrDefault("path");
                yield return new(name, text, fields.GetValueOrDefault("shell"));
            }
        }
    }

    private static Dictionary<string, string> Fields(List<Token> tokens, int start)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (tokens[start].Value != "{")
        {
            return result;
        }

        var depth = 0;
        for (var i = start; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!token.Literal && token.Value is "{" or "[" or "(")
            {
                depth++;
            }
            else if (!token.Literal && token.Value is "}" or "]" or ")")
            {
                if (--depth == 0)
                {
                    break;
                }
            }
            else if (depth == 1 && i + 3 < tokens.Count && tokens[i + 1].Value == ":"
                && tokens[i + 2].Literal && tokens[i + 2].Known && tokens[i + 3].Value is "," or "}")
            {
                result[token.Value] = tokens[i + 2].Value;
            }
        }

        return result;
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        for (var i = 0; i < text.Length;)
        {
            var c = text[i++];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (c == '/' && i < text.Length && text[i] == '/')
            {
                while (i < text.Length && text[i] != '\n') { i++; }
                continue;
            }

            if (c == '/' && i < text.Length && text[i] == '*')
            {
                var end = text.IndexOf("*/", i + 1, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 2;
                continue;
            }

            if (c is '\'' or '"' or '`')
            {
                var decoded = new StringBuilder();
                var closed = false;
                var dynamic = false;
                while (i < text.Length)
                {
                    var next = text[i++];
                    if (next == c) { closed = true; break; }
                    if (c == '`' && next == '$' && i < text.Length && text[i] == '{') { dynamic = true; }
                    if (next != '\\' || i >= text.Length) { decoded.Append(next); continue; }
                    var escape = text[i++];
                    if (escape is 'u' or 'x')
                    {
                        var digits = escape == 'u' ? 4 : 2;
                        if (i + digits <= text.Length && int.TryParse(text.AsSpan(i, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            decoded.Append((char)code);
                            i += digits;
                            continue;
                        }
                    }

                    if (escape == '\n') { continue; }
                    if (escape == '\r') { if (i < text.Length && text[i] == '\n') { i++; } continue; }
                    decoded.Append(escape switch { 'n' => '\n', 'r' => '\r', 't' => '\t', 'b' => '\b', 'f' => '\f', 'v' => '\v', '0' => '\0', _ => escape });
                }

                // Même une chaîne dynamique/incomplète reste opaque ; son contenu ne devient pas du code.
                tokens.Add(new Token(closed && !dynamic ? decoded.ToString() : "", Literal: true, Known: closed && !dynamic));
            }
            else if (char.IsLetterOrDigit(c) || c is '_' or '$')
            {
                var start = i - 1;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$')) { i++; }
                tokens.Add(new Token(text[start..i]));
            }
            else
            {
                tokens.Add(new Token(c.ToString()));
            }
        }

        return tokens;
    }
}
