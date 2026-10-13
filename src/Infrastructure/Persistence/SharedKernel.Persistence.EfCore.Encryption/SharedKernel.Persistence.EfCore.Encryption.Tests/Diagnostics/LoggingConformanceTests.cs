using System.Reflection;
using FluentAssertions;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.TestSupport;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Diagnostics;

/// <summary>
/// The logging of <c>SharedKernel.Persistence.EfCore.Encryption</c> follows the structure and template rules of the platform (one <c>EncryptionLog</c> class, one
/// <c>EncryptionLoggingEventIds</c> class, ids inside the package sub-block, one-sentence templates with quoted identifiers, a required
/// logger in every constructor), and every id is pinned by its literal so a renumbering shows up as a deliberate edit.
/// </summary>
public sealed class LoggingConformanceTests
{
    private static readonly Assembly Package = typeof(FieldEncryptionException).Assembly;

    private static readonly IReadOnlyDictionary<string, int> Pinned = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["KeyRefreshFailed"] = 6500,
        ["KeysStale"] = 6501,
        ["KeyFetchedOnMiss"] = 6502,
        ["KeyFetchOnMissFailed"] = 6503,
        ["StalePendingRestored"] = 6504,
        ["MaintenanceTargetStarted"] = 6510,
        ["MaintenanceBatchProcessed"] = 6511,
        ["MaintenanceCompleted"] = 6512,
        ["MaintenanceValuesSkipped"] = 6513,
        ["TenantKeyCreated"] = 6520,
        ["TenantKeyShredded"] = 6521,
        ["TenantShredIncomplete"] = 6522,
    };

    [Fact]
    public void TheLogging_FollowsTheStructureAndTemplateRules() =>
        LoggingConformance.Violations(Package, "Encryption", 5).Should().BeEmpty();

    [Fact]
    public void EveryEventId_IsPinnedByItsLiteral() =>
        LoggingConformance.Scan(Package).ToDictionary(m => m.Method.Name, m => m.EventId).Should().BeEquivalentTo(Pinned);

    [Fact]
    public void NoConstructor_TakesAnOptionalOrNullableLogger() =>
        LoggingConformance.OptionalLoggerConstructors(Package).Should().BeEmpty();
}
