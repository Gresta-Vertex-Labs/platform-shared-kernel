using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;
using SharedKernel.Security.Mtls.Diagnostics;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Logging;

public sealed class MtlsLogConformanceTests
{
    private const int SubBlockIndex = 3;

    private static readonly InvalidOperationException Failure = new("failure");

    public static TheoryData<int, int, LogLevel, string, bool> Events => new()
    {
        { 12300, MtlsLoggingEventIds.CertificateRejected, LogLevel.Warning, "The client certificate was rejected with rejection code '{RejectionCode}' for thumbprint '{Thumbprint}'.", false },
        { 12301, MtlsLoggingEventIds.ValidatorFailed, LogLevel.Error, "The certificate validator failed, so the client certificate was rejected for thumbprint '{Thumbprint}'.", true },
    };

    [Fact]
    public void Base_IsSubBlockThreeOfTheSecurityBlock_NotTheServiceDefaultsBlock() =>
        Assert.Equal(12300, MtlsLoggingEventIds.Base);

    [Theory]
    [MemberData(nameof(Events))]
    public void Event_KeepsItsPublishedIdLevelTemplateAndExceptionShape(int literalId, int constantId, LogLevel level, string template, bool hasException)
    {
        IReadOnlyList<LogRecord> records = DriveAll().Records;
        var eventId = new EventId(literalId);

        Assert.Equal(literalId, constantId);
        Assert.True(LoggingEventIdRanges.IsInSubBlock(literalId, LoggingEventIdRanges.Security, SubBlockIndex));
        records.ShouldHaveLogged(eventId, level);
        records.ShouldHaveLoggedTemplate(eventId, template);
        if (hasException)
        {
            records.ShouldHaveLoggedWithException(eventId, typeof(InvalidOperationException));
        }
        else
        {
            records.ShouldHaveLoggedWithoutException(eventId);
        }
    }

    [Fact]
    public void EveryEvent_FollowsTheLogConventionAndStaysInItsSubBlock()
    {
        IReadOnlyList<LogRecord> records = DriveAll().Records;

        Assert.Equal(Events.Count, records.Count);
        records.ShouldFollowLogConvention();
        records.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Security, SubBlockIndex);
    }

    [Fact]
    public void Conformance_EventInTheServiceDefaultsBlock_IsDetected()
    {
        var logger = new InMemoryLogger();
        logger.Log(LogLevel.Warning, new EventId(13300), "state", null, (s, _) => s);

        Assert.Throws<InvalidOperationException>(() =>
            logger.Records.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Security, SubBlockIndex));
    }

    private static InMemoryLogger DriveAll()
    {
        var logger = new InMemoryLogger();
        MtlsLog.CertificateRejected(logger, MtlsRejectionReasons.Rejected, "thumbprint");
        MtlsLog.ValidatorFailed(logger, "thumbprint", Failure);
        return logger;
    }
}
