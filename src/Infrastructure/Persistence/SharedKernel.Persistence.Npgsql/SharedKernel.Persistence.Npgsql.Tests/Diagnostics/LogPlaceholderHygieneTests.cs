using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Persistence.Npgsql.Tests.Diagnostics;

/// <summary>
/// No <c>[LoggerMessage]</c> template of the package names a tenant, actor, key or exception-type placeholder:
/// those values never reach a log (the ambient request context carries correlation and tenant).
/// </summary>
public sealed partial class LogPlaceholderHygieneTests
{
    private static readonly HashSet<string> Banned = new(StringComparer.OrdinalIgnoreCase)
    {
        "ActorId", "ActorTenantId", "ChainTenant", "TenantId", "IdempotencyKey", "ExceptionType", "MasterKeyId", "CorrelationId",
    };

    [GeneratedRegex(@"\{@?(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex Placeholder();

    [Fact]
    public void NoLoggerMessageTemplate_UsesABannedPlaceholder()
    {
        var violations = new List<string>();
        var assembly = typeof(SharedKernel.Persistence.Npgsql.Errors.PostgresExceptionClassifier).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var attribute = method.GetCustomAttribute<LoggerMessageAttribute>();
                if (attribute?.Message is not { } template)
                    continue;

                foreach (Match match in Placeholder().Matches(template))
                {
                    if (Banned.Contains(match.Groups["name"].Value))
                        violations.Add($"{type.Name}.{method.Name}: {{{match.Groups["name"].Value}}}");
                }
            }
        }

        violations.Should().BeEmpty();
    }
}
