using System.Reflection;
using FluentAssertions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.TestSupport;

namespace SharedKernel.Persistence.Abstractions.Tests.Diagnostics;

/// <summary>
/// The logging of <c>SharedKernel.Persistence.Abstractions</c> follows the structure and template rules of the platform (one <c>AbstractionsLog</c> class, one
/// <c>AbstractionsLoggingEventIds</c> class, ids inside the package sub-block, one-sentence templates with quoted identifiers, a required
/// logger in every constructor), and every id is pinned by its literal so a renumbering shows up as a deliberate edit.
/// </summary>
public sealed class LoggingConformanceTests
{
    private static readonly Assembly Package = typeof(CrossTenantScope).Assembly;

    private static readonly IReadOnlyDictionary<string, int> Pinned = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["CrossTenantScopeEntered"] = 6150,
    };

    [Fact]
    public void TheLogging_FollowsTheStructureAndTemplateRules() =>
        LoggingConformance.Violations(Package, "Abstractions", 1).Should().BeEmpty();

    [Fact]
    public void EveryEventId_IsPinnedByItsLiteral() =>
        LoggingConformance.Scan(Package).ToDictionary(m => m.Method.Name, m => m.EventId).Should().BeEquivalentTo(Pinned);

    [Fact]
    public void NoConstructor_TakesAnOptionalOrNullableLogger() =>
        LoggingConformance.OptionalLoggerConstructors(Package).Should().BeEmpty();
}
