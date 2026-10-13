using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.TestSupport;

/// <summary>One <c>[LoggerMessage]</c> method found by <see cref="LoggingConformance"/>.</summary>
internal sealed record LoggerMessageInfo(Type Owner, MethodInfo Method, int EventId, LogLevel Level, string Template);

/// <summary>
/// The structural rules every persistence package's logging follows (root rules L1-L8), checked by reflection over the
/// package assembly. Compiled into each persistence test project (this folder is linked, not a project), so a rule change is
/// one edit and a package cannot drift from its siblings.
/// </summary>
internal static partial class LoggingConformance
{
    /// <summary>Placeholder names that can carry free text, an identity or a provider detail (L3, L5).</summary>
    private static readonly HashSet<string> BannedPlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Error", "ErrorMessage", "Reason", "Detail", "Message", "Problem", "Finding",
        "ActorId", "ActorTenantId", "ChainTenant", "TenantId", "IdempotencyKey", "ExceptionType", "MasterKeyId",
        "CorrelationId", "TraceId", "SpanId", "ClientId", "Ip", "IpAddress", "Url", "Host",
    };

    private static readonly Regex Pascal = PascalPlaceholder();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex PascalPlaceholder();

    [GeneratedRegex(@"\{@?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:[:,][^}]*)?\}")]
    private static partial Regex Placeholder();

    /// <summary>Every <c>[LoggerMessage]</c> method of <paramref name="assembly"/>.</summary>
    public static IReadOnlyList<LoggerMessageInfo> Scan(Assembly assembly)
    {
        var found = new List<LoggerMessageInfo>();
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<LoggerMessageAttribute>() is { } attribute && attribute.Message is { } template)
                    found.Add(new LoggerMessageInfo(type, method, attribute.EventId, attribute.Level, template));
            }
        }

        return found;
    }

    /// <summary>
    /// Checks one package: one internal static <c>{prefix}Log</c> class under a <c>Diagnostics</c> namespace, one
    /// <c>{prefix}LoggingEventIds</c> class whose <c>Base</c> starts the package's 100-wide sub-block, every id inside that
    /// sub-block and unique, plain static methods taking the logger first and the exception last, and templates that are one
    /// sentence ending in a period with PascalCase placeholders, no banned name and quoted identifiers.
    /// </summary>
    /// <param name="assembly">The package assembly.</param>
    /// <param name="prefix">The package prefix (<c>EfCore</c> for <c>EfCoreLog</c>).</param>
    /// <param name="subBlockIndex">The package's sub-block index inside the persistence block (0 EfCore, 1 Abstractions, 3 Npgsql, 4 Dapper, 5 Encryption, 7 Auditing).</param>
    /// <returns>One line per violation; empty when the package conforms.</returns>
    public static IReadOnlyList<string> Violations(Assembly assembly, string prefix, int subBlockIndex)
    {
        var violations = new List<string>();
        var messages = Scan(assembly);
        var logName = prefix + "Log";
        var idsName = prefix + "LoggingEventIds";

        foreach (var owner in messages.Select(m => m.Owner).Distinct())
        {
            if (owner.Name != logName)
                violations.Add($"{owner.FullName}: logging class must be named '{logName}'.");
            if (owner.IsPublic || !(owner.IsAbstract && owner.IsSealed))
                violations.Add($"{owner.FullName}: logging class must be internal and static.");
            if (owner.Namespace is null || !owner.Namespace.EndsWith(".Diagnostics", StringComparison.Ordinal))
                violations.Add($"{owner.FullName}: logging class must live in a '.Diagnostics' namespace.");
        }

        var ids = assembly.GetTypes().FirstOrDefault(t => t.Name == idsName);
        if (ids is null)
        {
            violations.Add($"{assembly.GetName().Name}: missing '{idsName}'.");
        }
        else
        {
            if (ids.IsPublic || !(ids.IsAbstract && ids.IsSealed))
                violations.Add($"{ids.FullName}: must be internal and static.");

            var baseField = ids.GetField("Base", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            var expectedBase = LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Persistence, subBlockIndex);
            if (baseField?.GetRawConstantValue() is not int declared || declared != expectedBase)
                violations.Add($"{ids.FullName}: Base must be the first id of sub-block {subBlockIndex} ({expectedBase}).");
        }

        foreach (var duplicate in messages.GroupBy(m => m.EventId).Where(g => g.Count() > 1))
            violations.Add($"event id {duplicate.Key} is used by {string.Join(", ", duplicate.Select(m => m.Method.Name))}.");

        foreach (var message in messages)
        {
            var name = $"{message.Owner.Name}.{message.Method.Name}";

            if (!LoggingEventIdRanges.IsInSubBlock(message.EventId, LoggingEventIdRanges.Persistence, subBlockIndex))
                violations.Add($"{name}: event id {message.EventId} is outside the package sub-block.");

            var parameters = message.Method.GetParameters();
            if (!message.Method.IsStatic)
                violations.Add($"{name}: must be a plain static method, not an extension or instance method.");
            if (message.Method.GetCustomAttribute<System.Runtime.CompilerServices.ExtensionAttribute>() is not null)
                violations.Add($"{name}: must not be an extension method.");
            if (parameters.Length == 0 || parameters[0].ParameterType != typeof(ILogger))
                violations.Add($"{name}: the first parameter must be ILogger.");

            var exceptionIndex = Array.FindIndex(parameters, p => typeof(Exception).IsAssignableFrom(p.ParameterType));
            if (exceptionIndex >= 0 && exceptionIndex != parameters.Length - 1)
                violations.Add($"{name}: the Exception parameter must be last.");

            CheckTemplate(name, message.Template, violations);
        }

        return violations;
    }

    /// <summary>The template rules alone, for tests that pin a single message.</summary>
    public static IReadOnlyList<string> TemplateViolations(string template)
    {
        var violations = new List<string>();
        CheckTemplate("template", template, violations);
        return violations;
    }

    private static void CheckTemplate(string name, string template, List<string> violations)
    {
        if (!template.EndsWith('.'))
            violations.Add($"{name}: the template must end with a period.");
        if (template.Contains('—') || template.Contains('–'))
            violations.Add($"{name}: the template must not contain a dash.");
        if (template.Contains(". ", StringComparison.Ordinal))
            violations.Add($"{name}: the template must be one sentence.");
        if (template.Length > 0 && !char.IsUpper(template[0]))
            violations.Add($"{name}: the template must start with a capital letter.");

        foreach (Match match in Placeholder().Matches(template))
        {
            var placeholder = match.Groups["name"].Value;
            if (!Pascal.IsMatch(placeholder))
                violations.Add($"{name}: placeholder '{placeholder}' must be PascalCase.");
            if (BannedPlaceholders.Contains(placeholder))
                violations.Add($"{name}: placeholder '{placeholder}' is banned.");

            var identifier = placeholder.EndsWith("Type", StringComparison.Ordinal)
                || placeholder.EndsWith("Types", StringComparison.Ordinal)
                || placeholder.EndsWith("Name", StringComparison.Ordinal)
                || placeholder.EndsWith("Key", StringComparison.Ordinal)
                || placeholder.EndsWith("KeyId", StringComparison.Ordinal)
                || placeholder.EndsWith("Target", StringComparison.Ordinal);
            var quoted = match.Index > 0
                && template[match.Index - 1] == '\''
                && match.Index + match.Length < template.Length
                && template[match.Index + match.Length] == '\'';
            if (identifier && !quoted)
                violations.Add($"{name}: identifier placeholder '{placeholder}' must be quoted.");
        }
    }

    /// <summary>
    /// Constructors of the package's types whose logger parameter is optional or nullable. Every DI-built type takes its
    /// logger as a required dependency; <paramref name="exempt"/> names the types that are built outside the container.
    /// </summary>
    public static IReadOnlyList<string> OptionalLoggerConstructors(Assembly assembly, params string[] exempt)
    {
        var offenders = new List<string>();
        var context = new NullabilityInfoContext();
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || exempt.Contains(type.Name, StringComparer.Ordinal))
                continue;

            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    var isLogger = parameter.ParameterType == typeof(ILogger)
                        || parameter.ParameterType == typeof(ILoggerFactory)
                        || (parameter.ParameterType.IsGenericType && parameter.ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>));
                    if (!isLogger)
                        continue;

                    if (parameter.HasDefaultValue || context.Create(parameter).WriteState == NullabilityState.Nullable)
                        offenders.Add($"{type.FullName}({parameter.ParameterType.Name} {parameter.Name})");
                }
            }
        }

        return offenders;
    }
}
