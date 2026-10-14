using System.Reflection;
using FluentAssertions;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.TestSupport;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Diagnostics;

/// <summary>
/// The logging of <c>SharedKernel.Persistence.EfCore.Auditing</c> follows the structure and template rules of the platform (one <c>AuditingLog</c> class, one
/// <c>AuditingLoggingEventIds</c> class, ids inside the package sub-block, one-sentence templates with quoted identifiers, a required
/// logger in every constructor), and every id is pinned by its literal so a renumbering shows up as a deliberate edit.
/// </summary>
public sealed class LoggingConformanceTests
{
    private static readonly Assembly Package = typeof(AuditChainIntegrityException).Assembly;

    private static readonly IReadOnlyDictionary<string, int> Pinned = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["IdempotentDuplicateReturned"] = 6700,
        ["FailureAuditWriteFailed"] = 6701,
        ["ChainVerificationFailed"] = 6702,
        ["SealPassCompleted"] = 6703,
        ["SealRoundFailed"] = 6704,
        ["SealerLockHeldElsewhere"] = 6705,
        ["CheckpointEmitted"] = 6706,
        ["CheckpointEmissionFailed"] = 6707,
        ["SelfCheckFinding"] = 6708,
        ["SelfCheckFailed"] = 6709,
        ["SelfCheckPassed"] = 6710,
        ["PayloadsErased"] = 6711,
        ["ChainsResealed"] = 6712,
        ["SealerStarted"] = 6713,
    };

    [Fact]
    public void TheLogging_FollowsTheStructureAndTemplateRules() =>
        LoggingConformance.Violations(Package, "Auditing", 7).Should().BeEmpty();

    [Fact]
    public void EveryEventId_IsPinnedByItsLiteral() =>
        LoggingConformance.Scan(Package).ToDictionary(m => m.Method.Name, m => m.EventId).Should().BeEquivalentTo(Pinned);

    [Fact]
    public void NoConstructor_TakesAnOptionalOrNullableLogger() =>
        LoggingConformance.OptionalLoggerConstructors(Package).Should().BeEmpty();
}
