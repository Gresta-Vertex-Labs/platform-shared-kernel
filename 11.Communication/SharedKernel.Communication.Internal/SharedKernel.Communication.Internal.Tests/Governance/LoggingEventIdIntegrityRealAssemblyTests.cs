using System.Reflection;
using SharedKernel.ArchitectureTests;
using SharedKernel.Communication.Internal.Resolvers;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Communication.Internal.Tests.Governance;

/// <summary>
/// Invokes <c>00.Governance</c>'s <see cref="LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange"/>
/// against the real, compiled <c>SharedKernel.Communication.Internal</c> assembly (T-28, WO-041 P-255) — now
/// that both cross-domain blockers (<c>01.Core</c> P-249 <see cref="LoggingEventIdRanges"/> and
/// <c>00.Governance</c> P-250 <see cref="LoggingEventIdIntegrityAssertion"/>) have shipped. Mirrors the
/// established real-assembly invocation pattern from <c>SharedKernel.Application.Tests</c> and
/// <c>SharedKernel.Messaging.MassTransit.Tests</c>.
/// </summary>
/// <remarks>
/// <c>SharedKernel.Communication.Internal</c> reserves <c>11300-11399</c> within the domain's
/// <c>11000-11999</c> block (<see cref="LoggingEventIdRanges.Communication"/>), populated today by
/// <see cref="KubernetesServiceEndpointResolver"/>'s eight <c>[LoggerMessage]</c> methods (11300-11307)
/// and <c>StaticServiceDiscoveryStartupWarning</c>'s one (11308) — nine total.
/// </remarks>
public sealed class LoggingEventIdIntegrityRealAssemblyTests
{
    [Fact]
    public void AssertGloballyUniqueAndInRange_RealInternalAssembly_Passes()
    {
        var internalAssembly = typeof(KubernetesServiceEndpointResolver).Assembly;

        var domainRange = (LoggingEventIdRanges.Communication, LoggingEventIdRanges.Communication + 999);

        var assemblyRanges = new Dictionary<Assembly, (int RangeMin, int RangeMax)>
        {
            [internalAssembly] = domainRange,
        };

        var act = () => LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(assemblyRanges);

        act.Should().NotThrow(
            "every [LoggerMessage] EventId in SharedKernel.Communication.Internal must be globally unique "
            + "and fall within the 11000-11999 11.Communication domain range reserved by 01.Core's "
            + "LoggingEventIdRanges.Communication registry");
    }

    // NOTE: a "both Grpc + Internal assemblies together, no cross-assembly collisions" test is NOT placed
    // here — SharedKernel.Communication.Internal must never reference SharedKernel.Communication.Grpc, even
    // from its test project (CommunicationLayeringRules.CommunicationInternalNeverReferencesOtherCommunicationPackages).
    // That combined assertion lives in SharedKernel.Communication.Grpc.Tests/Governance/ instead, since Grpc's
    // production .csproj already legitimately references .Internal (service-discovery integration, G-09).
}
