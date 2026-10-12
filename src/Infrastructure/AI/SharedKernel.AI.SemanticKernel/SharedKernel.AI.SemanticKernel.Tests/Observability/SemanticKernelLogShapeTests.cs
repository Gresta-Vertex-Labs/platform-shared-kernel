using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.AI.SemanticKernel.Logging;
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
}
