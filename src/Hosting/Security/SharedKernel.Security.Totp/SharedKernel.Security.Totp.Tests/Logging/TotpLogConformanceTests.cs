using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;
using SharedKernel.Security.Totp.Diagnostics;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Logging;

public sealed class TotpLogConformanceTests
{
    private const int SubBlockIndex = 4;

    public static TheoryData<int, int, LogLevel, string> Events => new()
    {
        { 12400, TotpLoggingEventIds.ChallengeNotAccepted, LogLevel.Warning, "The TOTP challenge was not accepted with result '{Result}' during operation '{Operation}'." },
        { 12401, TotpLoggingEventIds.StepUpRecorded, LogLevel.Information, "The TOTP step-up completed during operation '{Operation}'." },
        { 12402, TotpLoggingEventIds.RecoveryCodeRedeemed, LogLevel.Information, "A recovery code was redeemed and {Remaining} unused recovery codes remain." },
    };

    [Fact]
    public void Base_IsSubBlockFourOfTheSecurityBlock() =>
        Assert.Equal(12400, TotpLoggingEventIds.Base);

    [Theory]
    [MemberData(nameof(Events))]
    public void Event_KeepsItsPublishedIdLevelAndTemplateAndCarriesNoException(int literalId, int constantId, LogLevel level, string template)
    {
        IReadOnlyList<LogRecord> records = DriveAll().Records;
        var eventId = new EventId(literalId);

        Assert.Equal(literalId, constantId);
        Assert.True(LoggingEventIdRanges.IsInSubBlock(literalId, LoggingEventIdRanges.Security, SubBlockIndex));
        records.ShouldHaveLogged(eventId, level);
        records.ShouldHaveLoggedTemplate(eventId, template);
        records.ShouldHaveLoggedWithoutException(eventId);
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
    public void Conformance_EventOutsideTheSubBlock_IsDetected()
    {
        var logger = new InMemoryLogger();
        logger.Log(LogLevel.Information, new EventId(12100), "state", null, (s, _) => s);

        Assert.Throws<InvalidOperationException>(() =>
            logger.Records.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Security, SubBlockIndex));
    }

    private static InMemoryLogger DriveAll()
    {
        var logger = new InMemoryLogger();
        TotpLog.ChallengeNotAccepted(logger, TotpChallengeResult.Replayed, "VerifyCode");
        TotpLog.StepUpRecorded(logger, "VerifyCode");
        TotpLog.RecoveryCodeRedeemed(logger, 4);
        return logger;
    }
}
