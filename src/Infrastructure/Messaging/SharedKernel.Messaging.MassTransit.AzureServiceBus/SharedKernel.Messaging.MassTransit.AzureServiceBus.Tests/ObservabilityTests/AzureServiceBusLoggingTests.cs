using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.MassTransit.AzureServiceBus.DeadLetter;
using SharedKernel.Messaging.MassTransit.AzureServiceBus.Diagnostics;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Messaging.MassTransit.AzureServiceBus.Tests.ObservabilityTests;

/// <summary>
/// The Azure Service Bus package owns sub-block 2 of the messaging block (7200-7299): its one advisory moved there from
/// 7010, which stays retired, and its template follows the log conventions.
/// </summary>
public sealed class AzureServiceBusLoggingTests
{
    private const int SubBlockIndex = 2;

    [Fact]
    public async Task TheDeadLetterAdvisory_LogsAWarning_AtTheFirstIdOfTheAzureSubBlock_7200()
    {
        var logger = new InMemoryLogger<DeadLetterPolicyAdvisoryHostedService>();

        await new DeadLetterPolicyAdvisoryHostedService(logger).StartAsync(CancellationToken.None);

        var record = logger.Records.ShouldHaveLogged(new EventId(7200), LogLevel.Warning);
        record.EventId.Id.Should().Be(AzureServiceBusLoggingEventIds.DeadLetterPolicyIgnored);
        logger.Records.ShouldHaveLoggedWithoutException(new EventId(7200));
        logger.Records.ShouldFollowLogConvention();
        logger.Records.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Messaging, SubBlockIndex);
    }

    [Fact]
    public void EveryLoggerMessageId_LiesInTheAzureSubBlock_AndTheRetiredCoreIdIsUnused()
    {
        var ids = typeof(AzureServiceBusLog).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.EventId)
            .ToArray();

        ids.Should().NotBeEmpty();
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Messaging, SubBlockIndex));
        ids.Should().NotContain(7010, "7010 is retired in the core's sub-block and never reused");
        AzureServiceBusLoggingEventIds.Base.Should().Be(LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Messaging, SubBlockIndex));
    }
}
