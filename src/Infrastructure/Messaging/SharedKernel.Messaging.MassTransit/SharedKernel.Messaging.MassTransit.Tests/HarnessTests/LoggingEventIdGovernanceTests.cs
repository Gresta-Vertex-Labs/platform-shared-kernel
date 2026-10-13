using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// EventId ownership of <c>SharedKernel.Messaging.MassTransit</c>: it owns sub-block 0 of the messaging block (7000-7099),
/// every <c>[LoggerMessage]</c> id comes from <see cref="MessagingLoggingEventIds"/>, and the retired ids are never reused.
/// </summary>
public sealed class LoggingEventIdGovernanceTests
{
    private const int SubBlockIndex = 0;

    private static readonly int[] RetiredIds = [7006, 7007, 7010];

    [Fact]
    public void EveryLoggerMessageId_LiesInTheCoreSubBlock_AndIsUnique()
    {
        var ids = LoggerMessageIds();

        ids.Should().NotBeEmpty();
        ids.Should().OnlyContain(
            id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Messaging, SubBlockIndex),
            "the core package owns 7000-7099 of the messaging block");
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheEventIdHolder_DeclaresEveryLoggerMessageId_AndNoOther()
    {
        var declared = typeof(MessagingLoggingEventIds)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => f.IsLiteral && f.Name != nameof(MessagingLoggingEventIds.Base))
            .Select(f => (int)f.GetRawConstantValue()!)
            .Order()
            .ToArray();

        declared.Should().Equal(LoggerMessageIds().Order());
        MessagingLoggingEventIds.Base.Should().Be(LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Messaging, SubBlockIndex));
    }

    [Fact]
    public void RetiredIds_AreNeverReassigned()
    {
        LoggerMessageIds().Should().NotIntersectWith(RetiredIds, "7006, 7007 and 7010 are retired for good");
    }

    [Fact]
    public void TheAzureServiceBusSubBlock_DoesNotOverlapTheCoreSubBlock()
    {
        var core = Enumerable.Range(LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Messaging, 0), 100);
        var azure = Enumerable.Range(LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Messaging, 2), 100);

        core.Intersect(azure).Should().BeEmpty();
    }

    private static int[] LoggerMessageIds() =>
    [
        .. typeof(MessagingLog).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.EventId),
    ];
}
