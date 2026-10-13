using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace SharedKernel.Persistence.EfCore.Tests.Diagnostics;

/// <summary>
/// Source gates over the shipped persistence packages (root rules E3 and C1/C2): every message handed to an exception or an
/// <c>Error</c> is a sentence ending in a period without a dash, "cannot" or an inner <c>.Message</c>, and every error code
/// is declared once, in <c>PersistenceErrorCodes</c>. The scan reads source, so it also covers messages no test throws.
/// </summary>
public sealed partial class ShippedMessageSourceGateTests
{
    // The files that legitimately hold a dotted "persistence.*" literal: the code registry and the tag-key declarations.
    private static readonly string[] CodeAndTagFiles =
    [
        "PersistenceErrorCodes.cs",
        "PersistenceTagKeys.cs",
        "CrossTenantScopeDiagnostics.cs",
    ];

    [GeneratedRegex(@"new\s+[A-Za-z_][\w.<>]*Exception\s*\(|Error\.(?:Validation|NotFound|Conflict|Forbidden|Unauthorized|Unexpected|Unavailable|Timeout|BusinessRule)\s*\(|ValidateOptionsResult\.Fail\s*\(")]
    private static partial Regex MessageSite();

    [GeneratedRegex("\"(?<code>[Pp]ersistence\\.[A-Za-z0-9_.]+)\"")]
    private static partial Regex CodeLiteral();

    private static string FindPersistenceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Infrastructure", "Persistence");
            if (File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")) && Directory.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }

    private static IEnumerable<string> ShippedSources() =>
        Directory.EnumerateFiles(FindPersistenceRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = path.Replace('\\', '/');
                return !relative.Contains(".Tests/", StringComparison.Ordinal)
                    && !relative.Contains("/obj/", StringComparison.Ordinal)
                    && !relative.Contains("/bin/", StringComparison.Ordinal)
                    && !relative.Contains("SharedKernel.Persistence.ConsumerVerify/", StringComparison.Ordinal)
                    && !relative.Contains("/SharedTestSupport/", StringComparison.Ordinal);
            });

    [Fact]
    public void EveryMessageOfAShippedException_OrError_IsASentenceEndingInAPeriod_WithoutADashCannotOrInnerMessage()
    {
        var offenders = new List<string>();
        foreach (var path in ShippedSources())
        {
            var text = File.ReadAllText(path);
            foreach (Match site in MessageSite().Matches(text))
            {
                if (IsInComment(text, site.Index))
                    continue;

                foreach (var message in MessagesOf(text, site.Index + site.Length))
                {
                    var where = $"{Path.GetFileName(path)}: \"{(message.Length > 70 ? message[..70] : message)}\"";
                    // A message that ends in an interpolation hole ends in the sentence the hole carries (a fixed reason).
                    if (!message.EndsWith('.') && !message.EndsWith("{}", StringComparison.Ordinal))
                        offenders.Add(where + " does not end with a period");
                    if (message.Contains('—') || message.Contains('–'))
                        offenders.Add(where + " contains a dash");
                    if (Regex.IsMatch(message, @"\bcannot\b|\bcan not\b", RegexOptions.IgnoreCase))
                        offenders.Add(where + " says 'cannot'; use 'must not' or state what happens");
                    if (message.Contains(".Message}", StringComparison.Ordinal) || message.Contains("ex.Message", StringComparison.Ordinal))
                        offenders.Add(where + " embeds an inner exception message");
                }
            }
        }

        offenders.Should().BeEmpty("a shipped message names the remedy in sentences that end in a period (E3)");
    }

    [Fact]
    public void NoShippedSource_DeclaresAnErrorCode_OutsidePersistenceErrorCodes()
    {
        var offenders = new List<string>();
        foreach (var path in ShippedSources())
        {
            if (CodeAndTagFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal))
                continue;

            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                foreach (Match code in CodeLiteral().Matches(line))
                    offenders.Add($"{Path.GetFileName(path)}: \"{code.Groups["code"].Value}\"");
            }
        }

        offenders.Should().BeEmpty("every persistence error code lives in PersistenceErrorCodes, never inline (C2)");
    }

    private static bool IsInComment(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        return text.AsSpan(lineStart, index - lineStart).TrimStart().StartsWith("//", StringComparison.Ordinal);
    }

    // The text of every top-level argument, starting after the opening parenthesis, that is only string literals joined by
    // '+': interpolation holes become "{}". Other arguments (names, Errors, exceptions) are skipped.
    private static IEnumerable<string> MessagesOf(string text, int start)
    {
        var depth = 0;
        var argumentStart = start;
        var i = start;
        var messages = new List<string>();
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"' || (c == '$' && i + 1 < text.Length && text[i + 1] == '"') || (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
                || ((c == '$' || c == '@') && i + 2 < text.Length && text[i + 2] == '"'))
            {
                i = SkipString(text, i, out _);
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    AddIfMessage(text[argumentStart..i], messages);
                    break;
                }

                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                AddIfMessage(text[argumentStart..i], messages);
                argumentStart = i + 1;
            }

            i++;
        }

        return messages;
    }

    private static void AddIfMessage(string argument, List<string> messages)
    {
        var parts = new StringBuilder();
        var i = 0;
        var any = false;
        while (i < argument.Length)
        {
            var c = argument[i];
            if (char.IsWhiteSpace(c) || c == '+')
            {
                i++;
                continue;
            }

            if (c is '"' or '$' or '@')
            {
                var end = SkipString(argument, i, out var content);
                parts.Append(content);
                any = true;
                i = end;
                continue;
            }

            return; // not purely string literals: a name, a call, an Error value
        }

        if (any)
            messages.Add(parts.ToString());
    }

    // Skips the string literal starting at index (regular, interpolated, verbatim) and returns the index after it; the
    // content has interpolation holes replaced by "{}" and escaped quotes unescaped.
    private static int SkipString(string text, int index, out string content)
    {
        var interpolated = false;
        var verbatim = false;
        var i = index;
        while (text[i] != '"')
        {
            if (text[i] == '$')
                interpolated = true;
            if (text[i] == '@')
                verbatim = true;
            i++;
        }

        i++; // opening quote
        var builder = new StringBuilder();
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == '"')
                {
                    builder.Append('"');
                    i += 2;
                    continue;
                }

                i++;
                break;
            }

            if (!verbatim && c == '\\' && i + 1 < text.Length)
            {
                builder.Append(text[i + 1] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', var other => other });
                i += 2;
                continue;
            }

            if (interpolated && c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    builder.Append('{');
                    i += 2;
                    continue;
                }

                builder.Append("{}");
                i = SkipHole(text, i + 1);
                continue;
            }

            if (interpolated && c == '}' && i + 1 < text.Length && text[i + 1] == '}')
            {
                builder.Append('}');
                i += 2;
                continue;
            }

            builder.Append(c);
            i++;
        }

        content = builder.ToString();
        return i;
    }

    // Skips an interpolation hole (after its opening brace), including nested strings and braces.
    private static int SkipHole(string text, int index)
    {
        var depth = 1;
        var i = index;
        while (i < text.Length && depth > 0)
        {
            var c = text[i];
            if (c == '"' || ((c == '$' || c == '@') && i + 1 < text.Length && (text[i + 1] == '"' || text[i + 1] == '$' || text[i + 1] == '@')))
            {
                i = SkipString(text, i, out _);
                continue;
            }

            if (c == '{')
                depth++;
            else if (c == '}')
                depth--;
            i++;
        }

        return i;
    }
}
