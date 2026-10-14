using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.AI.SemanticKernel.Diagnostics;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.AI.SemanticKernel.Tests.Observability;

/// <summary>The log catalogue of this package: ids stay inside the sub-block, none is added or renumbered, and the Phase 1 shape holds.</summary>
public sealed class SemanticKernelLogShapeTests
{
    private static IReadOnlyList<LoggerMessageAttribute> Attributes() =>
    [
        .. typeof(SemanticKernelLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.GetCustomAttributes(typeof(LoggerMessageAttribute), inherit: false).Cast<LoggerMessageAttribute>().Single()),
    ];

    [Fact]
    public void EveryId_IsUnique_InsideTheSemanticKernelSubBlock_AndNoneWasAdded()
    {
        var attributes = Attributes();

        attributes.Select(a => a.EventId).Should().OnlyHaveUniqueItems();
        attributes.Select(a => a.EventId).Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Intelligence, 3));
        attributes.Select(a => a.EventId).Should().BeEquivalentTo(Enumerable.Range(10300, 10));
    }

    [Fact]
    public void Templates_CarryNoFreeTextReason_NoHost_AndNoPort()
    {
        Attributes().Select(a => a.Message).Should().NotContain(m => m!.Contains("{Reason}") || m.Contains("{Host}") || m.Contains("{Port}"));
    }

    [Fact]
    public void TheClassifiedFaultLog_IsAWarning_AndTheRejectionLogCarriesTypeAndCode()
    {
        var attributes = Attributes();

        attributes.Single(a => a.EventId == 10307).Level.Should().Be(LogLevel.Warning);
        attributes.Single(a => a.EventId == 10305).Message.Should().Contain("{ErrorType}").And.Contain("{ErrorCode}");
        attributes.Single(a => a.EventId == 10307).Message.Should().Contain("{ErrorCode}");
    }
    [Fact]
    public void EveryTemplate_IsOneSentenceEndingInAPeriod_WithPascalCasePlaceholders_AndNoBannedShape()
    {
        foreach (var attribute in Attributes())
        {
            var message = attribute.Message!;
            var outsideQuotes = System.Text.RegularExpressions.Regex.Replace(message, "'[^']*'", "''");
            message.Should().EndWith(".");
            System.Text.RegularExpressions.Regex.IsMatch(outsideQuotes, @"\.\s").Should().BeFalse($"'{message}' is one sentence");
            message.Should().NotContain("—").And.NotContain("=");
            var placeholders = System.Text.RegularExpressions.Regex.Matches(message, @"\{(?<n>[A-Za-z_]\w*)").Select(m => m.Groups["n"].Value).ToList();
            placeholders.Should().OnlyContain(p => char.IsUpper(p[0]));
            placeholders.Should().NotContain(["Error", "Reason", "ErrorMessage", "Detail", "DurationMs", "CorrelationId", "TraceId", "TenantId"]);
        }
    }

    [Fact]
    public void LogMethods_ArePlainStaticPartials_NotExtensionMethods()
    {
        typeof(SemanticKernelLog).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttributes(typeof(LoggerMessageAttribute), inherit: false).Length == 1)
            .Should().OnlyContain(m => !m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false));
    }
}
