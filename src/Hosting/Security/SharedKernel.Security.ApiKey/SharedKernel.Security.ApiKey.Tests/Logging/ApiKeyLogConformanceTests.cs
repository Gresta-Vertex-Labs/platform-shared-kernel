using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;
using SharedKernel.Security.ApiKey.Diagnostics;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Logging;

public sealed class ApiKeyLogConformanceTests
{
    private const int SubBlockIndex = 2;

    public static TheoryData<int, int, LogLevel, string> Events => new()
    {
        { 12200, ApiKeyLoggingEventIds.KeyRejected, LogLevel.Warning, "The API key was rejected with rejection code '{RejectionCode}' for key id '{KeyId}'." },
        { 12201, ApiKeyLoggingEventIds.AmbiguousKey, LogLevel.Warning, "The request carries more than one '{HeaderName}' header value." },
    };

    [Fact]
    public void Base_IsSubBlockTwoOfTheSecurityBlock() =>
        Assert.Equal(12200, ApiKeyLoggingEventIds.Base);

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
        logger.Log(LogLevel.Warning, new EventId(12300), "state", null, (s, _) => s);

        Assert.Throws<InvalidOperationException>(() =>
            logger.Records.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Security, SubBlockIndex));
    }

    [Fact]
    public void Reasons_AreTheDocumentedClosedCodes()
    {
        Assert.Equal(
            ["Rejected", "Malformed", "WrongPrefix", "UnknownKey", "Revoked", "Expired"],
            [
                ApiKeyRejectionReasons.Rejected,
                ApiKeyRejectionReasons.Malformed,
                ApiKeyRejectionReasons.WrongPrefix,
                ApiKeyRejectionReasons.UnknownKey,
                ApiKeyRejectionReasons.Revoked,
                ApiKeyRejectionReasons.Expired,
            ]);
    }

    private static InMemoryLogger DriveAll()
    {
        var logger = new InMemoryLogger();
        ApiKeyLog.KeyRejected(logger, ApiKeyRejectionReasons.Expired, "abcdefghijklmnop");
        ApiKeyLog.AmbiguousKey(logger, "X-Api-Key");
        return logger;
    }
}
