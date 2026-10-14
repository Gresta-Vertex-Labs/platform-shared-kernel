using System.Reflection;
using FluentAssertions;
using SharedKernel.Persistence.Dapper.Options;
using SharedKernel.Persistence.TestSupport;

namespace SharedKernel.Persistence.Dapper.Tests.Diagnostics;

/// <summary>
/// The logging of <c>SharedKernel.Persistence.Dapper</c> follows the structure and template rules of the platform (one <c>DapperLog</c> class, one
/// <c>DapperLoggingEventIds</c> class, ids inside the package sub-block, one-sentence templates with quoted identifiers, a required
/// logger in every constructor), and every id is pinned by its literal so a renumbering shows up as a deliberate edit.
/// </summary>
public sealed class LoggingConformanceTests
{
    private static readonly Assembly Package = typeof(DapperPersistenceOptions).Assembly;

    private static readonly IReadOnlyDictionary<string, int> Pinned = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["SessionRollbackFailed"] = 6400,
        ["CrossTenantSessionOpened"] = 6401,
    };

    [Fact]
    public void TheLogging_FollowsTheStructureAndTemplateRules() =>
        LoggingConformance.Violations(Package, "Dapper", 4).Should().BeEmpty();

    [Fact]
    public void EveryEventId_IsPinnedByItsLiteral() =>
        LoggingConformance.Scan(Package).ToDictionary(m => m.Method.Name, m => m.EventId).Should().BeEquivalentTo(Pinned);

    [Fact]
    public void NoConstructor_TakesAnOptionalOrNullableLogger() =>
        LoggingConformance.OptionalLoggerConstructors(Package).Should().BeEmpty();
}
