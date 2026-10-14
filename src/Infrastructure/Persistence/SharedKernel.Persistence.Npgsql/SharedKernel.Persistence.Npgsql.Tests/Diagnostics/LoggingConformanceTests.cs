using System.Reflection;
using FluentAssertions;
using SharedKernel.Persistence.Npgsql.Options;
using SharedKernel.Persistence.TestSupport;

namespace SharedKernel.Persistence.Npgsql.Tests.Diagnostics;

/// <summary>
/// The logging of <c>SharedKernel.Persistence.Npgsql</c> follows the structure and template rules of the platform (one <c>NpgsqlLog</c> class, one
/// <c>NpgsqlLoggingEventIds</c> class, ids inside the package sub-block, one-sentence templates with quoted identifiers, a required
/// logger in every constructor), and every id is pinned by its literal so a renumbering shows up as a deliberate edit.
/// </summary>
public sealed class LoggingConformanceTests
{
    private static readonly Assembly Package = typeof(NpgsqlPersistenceOptions).Assembly;

    private static readonly IReadOnlyDictionary<string, int> Pinned = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["InsecureSslMode"] = 6300,
        ["AdvisoryMigrationLockAcquired"] = 6301,
        ["AdvisoryMigrationLockTimedOut"] = 6302,
        ["AdvisoryMigrationLockReleased"] = 6303,
        ["RowLevelSecurityPrivilegeProblem"] = 6304,
        ["RowLevelSecurityPrivilegeCheckSkipped"] = 6305,
        ["RowLevelSecurityPrivilegeCheckPassed"] = 6306,
    };

    [Fact]
    public void TheLogging_FollowsTheStructureAndTemplateRules() =>
        LoggingConformance.Violations(Package, "Npgsql", 3).Should().BeEmpty();

    [Fact]
    public void EveryEventId_IsPinnedByItsLiteral() =>
        LoggingConformance.Scan(Package).ToDictionary(m => m.Method.Name, m => m.EventId).Should().BeEquivalentTo(Pinned);

    [Fact]
    public void NoConstructor_TakesAnOptionalOrNullableLogger() =>
        LoggingConformance.OptionalLoggerConstructors(Package).Should().BeEmpty();
}
