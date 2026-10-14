using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;
using SharedKernel.Security.Oidc.Diagnostics;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Logging;

public sealed class OidcLogConformanceTests
{
    private const int SubBlockIndex = 1;

    private static readonly InvalidOperationException Failure = new("failure");

    public static TheoryData<int, int, LogLevel, string, bool> Events => new()
    {
        { 12100, OidcLoggingEventIds.SubjectMissing, LogLevel.Warning, "The token names neither a subject nor a client id, so the caller is anonymous.", false },
        { 12101, OidcLoggingEventIds.TenantClaimInvalid, LogLevel.Warning, "The tenant claim '{TenantClaimType}' is not a valid GUID, so the caller has no tenant.", false },
        { 12102, OidcLoggingEventIds.DpopRejected, LogLevel.Warning, "DPoP validation failed with rejection code '{RejectionCode}'.", false },
        { 12103, OidcLoggingEventIds.TokenRevoked, LogLevel.Warning, "The access token was rejected because it is revoked.", false },
        { 12104, OidcLoggingEventIds.SigningAlgorithmRejected, LogLevel.Warning, "The signing algorithm '{Algorithm}' is not allowed.", false },
        { 12105, OidcLoggingEventIds.CertificateBindingRejected, LogLevel.Warning, "The certificate binding was rejected with rejection code '{RejectionCode}'.", false },
        { 12106, OidcLoggingEventIds.RevocationCacheFailed, LogLevel.Warning, "The revocation cache failed, so the revocation check is called directly.", true },
        { 12107, OidcLoggingEventIds.RevocationCheckFailed, LogLevel.Error, "The revocation check failed, so the access token was rejected.", true },
    };

    [Fact]
    public void Base_IsSubBlockOneOfTheSecurityBlock() =>
        Assert.Equal(12100, OidcLoggingEventIds.Base);

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
    public void Conformance_RenumberedEvent_IsDetectedBySubBlockAssertion()
    {
        var logger = new InMemoryLogger();
        logger.Log(LogLevel.Warning, new EventId(12200), "state", null, (s, _) => s);

        Assert.Throws<InvalidOperationException>(() =>
            logger.Records.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Security, SubBlockIndex));
    }

    private static InMemoryLogger DriveAll()
    {
        var logger = new InMemoryLogger();
        OidcLog.SubjectMissing(logger);
        OidcLog.TenantClaimInvalid(logger, "tenant_id");
        OidcLog.DpopRejected(logger, DpopRejectionReasons.Replayed);
        OidcLog.TokenRevocationRejected(logger);
        OidcLog.SigningAlgorithmRejected(logger, "HS256");
        OidcLog.CertificateBindingRejected(logger, CertificateBindingReasons.CertificateMismatch);
        OidcLog.RevocationCacheFailed(logger, Failure);
        OidcLog.RevocationCheckFailed(logger, Failure);
        return logger;
    }
}
