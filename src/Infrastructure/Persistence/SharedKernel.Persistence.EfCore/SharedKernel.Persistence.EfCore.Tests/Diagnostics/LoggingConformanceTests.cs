using System.Reflection;
using FluentAssertions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.TestSupport;

namespace SharedKernel.Persistence.EfCore.Tests.Diagnostics;

/// <summary>
/// The logging of <c>SharedKernel.Persistence.EfCore</c> follows the structure and template rules of the platform (one <c>EfCoreLog</c> class, one
/// <c>EfCoreLoggingEventIds</c> class, ids inside the package sub-block, one-sentence templates with quoted identifiers, a required
/// logger in every constructor), and every id is pinned by its literal so a renumbering shows up as a deliberate edit.
/// </summary>
public sealed class LoggingConformanceTests
{
    private static readonly Assembly Package = typeof(SharedKernelDbContext).Assembly;

    private static readonly IReadOnlyDictionary<string, int> Pinned = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["ConcurrencyConflictDetected"] = 6000,
        ["MigrationAndSeedStarted"] = 6001,
        ["SeederApplied"] = 6002,
        ["MigrationAndSeedCompleted"] = 6003,
        ["MigrationAndSeedFailed"] = 6004,
        ["AdvisoryLockAcquired"] = 6005,
        ["AdvisoryLockReleased"] = 6006,
        ["TransientRetryExhausted"] = 6008,
        ["DatabaseErrorClassified"] = 6010,
        ["NoMigrationLockRegistered"] = 6013,
        ["DomainEventsDiscarded"] = 6014,
        ["TenantIsolationViolationProven"] = 6015,
        ["NoDomainEventDispatcherRegistered"] = 6016,
        ["ConflictRowLookupFailed"] = 6017,
        ["ModelValidated"] = 6018,
        ["ContextCannotJoinTransaction"] = 6020,
        ["RolledBackAfterJoinedFailure"] = 6021,
        ["EntityVersionRejected"] = 6022,
        ["EntityVersionKeyRefreshFailed"] = 6023,
        ["EntityVersionNotSealed"] = 6024,
        ["EntityVersionKeyWarmUpFailed"] = 6025,
        ["EntityVersionKeyWarmUpTimedOut"] = 6026,
        ["CrossTenantConnectionUsed"] = 6027,
        ["TenantTableNotProtected"] = 6028,
        ["ModelValidationFailed"] = 6029,
    };

    [Fact]
    public void TheLogging_FollowsTheStructureAndTemplateRules() =>
        LoggingConformance.Violations(Package, "EfCore", 0).Should().BeEmpty();

    [Fact]
    public void EveryEventId_IsPinnedByItsLiteral() =>
        LoggingConformance.Scan(Package).ToDictionary(m => m.Method.Name, m => m.EventId).Should().BeEquivalentTo(Pinned);

    [Fact]
    public void NoConstructor_TakesAnOptionalOrNullableLogger() =>
        LoggingConformance.OptionalLoggerConstructors(Package, "PersistenceContextDependencies").Should().BeEmpty();
}
