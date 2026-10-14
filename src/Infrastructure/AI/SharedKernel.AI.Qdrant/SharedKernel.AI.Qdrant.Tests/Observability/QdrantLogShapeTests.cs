using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.AI.Qdrant.Diagnostics;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.AI.Qdrant.Tests.Observability;

/// <summary>The log catalogue of this package: ids stay inside the sub-block, none is renumbered, and every template follows the L3 shape.</summary>
public sealed class QdrantLogShapeTests
{
    private static IReadOnlyList<LoggerMessageAttribute> Attributes() =>
    [
        .. typeof(QdrantLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.GetCustomAttributes(typeof(LoggerMessageAttribute), inherit: false).Cast<LoggerMessageAttribute>().Single()),
    ];

    [Fact]
    public void EveryId_IsUnique_InsideTheQdrantSubBlock_NeverInTheReservedBlocks_AndNoneWasRenumbered()
    {
        var ids = Attributes().Select(a => a.EventId).ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Intelligence, 1));
        ids.Should().NotContain(id =>
            LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Intelligence, 0)
            || LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Intelligence, 2));
        ids.Should().BeEquivalentTo(Enumerable.Range(10100, 15));
    }

    [Fact]
    public void EveryTemplate_IsOneSentenceEndingInAPeriod_WithPascalCasePlaceholders_AndNoBannedShape()
    {
        foreach (var attribute in Attributes())
        {
            var message = attribute.Message!;
            var outsideQuotes = Regex.Replace(message, "'[^']*'", "''");
            message.Should().EndWith(".");
            Regex.IsMatch(outsideQuotes, @"\.\s").Should().BeFalse($"'{message}' is one sentence");
            message.Should().NotContain("—").And.NotContain("=");
            var placeholders = Regex.Matches(message, @"\{(?<n>[A-Za-z_]\w*)").Select(m => m.Groups["n"].Value).ToList();
            placeholders.Should().OnlyContain(p => char.IsUpper(p[0]));
            placeholders.Should().NotContain(["Error", "Reason", "ErrorMessage", "Detail", "DurationMs", "CorrelationId", "TraceId", "TenantId"]);
        }
    }

    [Fact]
    public void LogMethods_ArePlainStaticPartials_NotExtensionMethods()
    {
        typeof(QdrantLog).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttributes(typeof(LoggerMessageAttribute), inherit: false).Length == 1)
            .Should().OnlyContain(m => !m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false));
    }
}
