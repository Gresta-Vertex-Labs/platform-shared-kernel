using System.Reflection;
using FluentAssertions;
using SharedKernel.ArchitectureTests;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// LR-16: runs <see cref="LoggingEventIdIntegrityAssertion"/> (00.Governance, P-250) against the
/// real, built <c>SharedKernel.Messaging.MassTransit</c> assembly — verifies every
/// <c>[LoggerMessage]</c>-attributed <c>EventId</c> introduced by the P-254 retrofit is globally
/// unique and falls inside this domain's reserved <c>7000-7999</c> range
/// (<see cref="LoggingEventIdRanges.Messaging"/>).
/// </summary>
public sealed class LoggingEventIdGovernanceTests
{
    [Fact]
    public void MassTransitAssembly_AllLoggerMessageEventIds_AreGloballyUniqueAndInRange()
    {
        var assemblyRanges = new Dictionary<Assembly, (int RangeMin, int RangeMax)>
        {
            [typeof(SharedKernel.Messaging.MassTransit.Consumers.ConsumerBase<>).Assembly] =
                (LoggingEventIdRanges.Messaging, LoggingEventIdRanges.Messaging + 999),
        };

        var act = () => LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(assemblyRanges);

        act.Should().NotThrow(
            "every [LoggerMessage] EventId in SharedKernel.Messaging.MassTransit must be globally " +
            "unique and fall inside the 7000-7999 reserved range after the P-254 retrofit");
    }
}
