using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Testing.Logging;

/// <summary>
/// Test-side mirror of the log message conventions: L3 (template shape), L4 (exception parameter) and L5 (forbidden
/// placeholder names). Returns every violation so a conformance test lists the whole backlog. Enforcement stays in the
/// governance tests; this is for a domain's own suite.
/// </summary>
internal static partial class LogConventions
{
    private static readonly string[] ForbiddenDetailNames = ["Error", "Reason", "ErrorMessage", "Detail"];

    private static readonly HashSet<string> ForbiddenPlaceholders = new(StringComparer.Ordinal)
    {
        "TenantId", "CorrelationId", "TraceId", "SpanId", "ActorId", "ClientId", "UserId",
        "IpAddress", "Url", "Host", "IdempotencyKey", "ErrorMessage",
    };

    public static List<string> Violations(IEnumerable<LogRecord> records)
    {
        var violations = new List<string>();
        foreach (var record in records)
        {
            var label = $"EventId {record.EventId.Id}{(string.IsNullOrEmpty(record.EventId.Name) ? string.Empty : $" '{record.EventId.Name}'")}";
            CheckExceptionParameter(record, label, violations);

            var template = record.Template;
            if (template is null)
            {
                violations.Add($"{label} [L3] has no message template ({{OriginalFormat}}); log through a [LoggerMessage] method");
                continue;
            }

            CheckTemplate(template, label, violations);
        }

        return violations;
    }

    private static void CheckExceptionParameter(LogRecord record, string label, List<string> violations)
    {
        if (record.Exception is not null)
        {
            return;
        }

        if (record.LogLevel >= LogLevel.Error)
        {
            violations.Add($"{label} [L4] is logged at {record.LogLevel} without an exception parameter");
        }
        else if (record.State is not null && record.State.Any(p => p.Value is Exception))
        {
            violations.Add($"{label} [L4] carries an exception as a placeholder value instead of the exception parameter");
        }
    }

    private static void CheckTemplate(string template, string label, List<string> violations)
    {
        var text = template.Replace("{{", "  ", StringComparison.Ordinal).Replace("}}", "  ", StringComparison.Ordinal);

        if (!text.EndsWith('.') || SentenceBreak().IsMatch(text))
        {
            violations.Add($"{label} [L3] template '{template}' is not one sentence ending in a period");
        }

        if (text.Contains('—', StringComparison.Ordinal))
        {
            violations.Add($"{label} [L3] template '{template}' contains an em-dash");
        }

        if (KeyValueParenthetical().IsMatch(text))
        {
            violations.Add($"{label} [L3] template '{template}' contains a key=value parenthetical");
        }

        foreach (Match match in Placeholder().Matches(text))
        {
            var name = match.Groups["name"].Value;
            var bare = name.TrimStart('@', '$');

            if (!PascalCase().IsMatch(bare))
            {
                violations.Add($"{label} [L3] placeholder '{{{name}}}' is not PascalCase");
            }

            if (Array.IndexOf(ForbiddenDetailNames, bare) >= 0)
            {
                violations.Add($"{label} [L3] placeholder '{{{name}}}' is a forbidden detail placeholder");
            }

            if ((bare.EndsWith("Name", StringComparison.Ordinal) || bare.EndsWith("Key", StringComparison.Ordinal))
                && !IsQuoted(text, match))
            {
                violations.Add($"{label} [L3] identifier placeholder '{{{name}}}' is not wrapped in single quotes");
            }

            if (ForbiddenPlaceholders.Contains(bare))
            {
                violations.Add($"{label} [L5] placeholder '{{{name}}}' must flow ambiently, not as a log property");
            }
        }
    }

    private static bool IsQuoted(string text, Match match) =>
        match.Index > 0
        && text[match.Index - 1] == '\''
        && match.Index + match.Length < text.Length
        && text[match.Index + match.Length] == '\'';

    [GeneratedRegex(@"\{(?<name>[^{}:,]+)(?:[:,][^{}]*)?\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex PascalCase();

    [GeneratedRegex(@"[.!?]\s+\S")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"\([^)]*=[^)]*\)")]
    private static partial Regex KeyValueParenthetical();
}
