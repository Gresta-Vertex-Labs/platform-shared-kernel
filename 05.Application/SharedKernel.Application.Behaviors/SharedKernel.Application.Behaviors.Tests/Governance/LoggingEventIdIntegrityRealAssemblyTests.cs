using FluentAssertions;
using SharedKernel.ArchitectureTests;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Primitives.Logging;
using System.Reflection;

namespace SharedKernel.Application.Behaviors.Tests.Governance;

/// <summary>
/// Invokes <c>00.Governance</c>'s <see cref="LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange"/>
/// against the real, compiled <c>SharedKernel.Application</c>/<c>SharedKernel.Application.Behaviors</c>
/// assemblies (WO-041, T-65) — the first real-assembly invocation of this helper, now that both
/// cross-domain blockers (<c>01.Core</c> P-249 <see cref="LoggingEventIdRanges"/> and
/// <c>00.Governance</c> P-250 <see cref="LoggingEventIdIntegrityAssertion"/>) have shipped.
/// </summary>
/// <remarks>
/// <c>SharedKernel.Application</c> reserves <c>5000</c>-<c>5099</c> (currently zero <c>[LoggerMessage]</c>
/// call sites — passes trivially, an empty set has no collisions and no out-of-range members).
/// <c>SharedKernel.Application.Behaviors</c> reserves <c>5100</c>-<c>5199</c>, populated by this
/// phase's ten allocated <c>EventId</c>s (5100-5103, 5110-5111, 5120-5123). Both assemblies share the
/// same domain-level range tuple, since both are declared inside the <c>05.Application</c> domain.
/// </remarks>
public sealed class LoggingEventIdIntegrityRealAssemblyTests
{
    [Fact]
    public void AssertGloballyUniqueAndInRange_RealApplicationAndBehaviorsAssemblies_Passes()
    {
        var applicationAssembly = typeof(IDomainEventHandler<>).Assembly;
        var behaviorsAssembly = typeof(AuthorizationBehavior<,>).Assembly;

        var domainRange = (LoggingEventIdRanges.Application, LoggingEventIdRanges.Application + 999);

        var assemblyRanges = new Dictionary<Assembly, (int RangeMin, int RangeMax)>
        {
            [applicationAssembly] = domainRange,
            [behaviorsAssembly] = domainRange,
        };

        var act = () => LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(assemblyRanges);

        act.Should().NotThrow(
            "every [LoggerMessage] EventId in SharedKernel.Application/SharedKernel.Application.Behaviors "
            + "must be globally unique and fall within the 5000-5999 05.Application domain range reserved "
            + "by 01.Core's LoggingEventIdRanges.Application registry");
    }
}
