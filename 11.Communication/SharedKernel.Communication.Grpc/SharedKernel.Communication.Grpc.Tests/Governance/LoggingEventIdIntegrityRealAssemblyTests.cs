using System.Reflection;
using SharedKernel.ArchitectureTests;
using SharedKernel.Communication.Grpc.Interceptors;
using SharedKernel.Communication.Internal.Resolvers; // IServiceEndpointResolver — public type used only to anchor typeof(...).Assembly
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Communication.Grpc.Tests.Governance;

/// <summary>
/// Invokes <c>00.Governance</c>'s <see cref="LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange"/>
/// against the real, compiled <c>SharedKernel.Communication.Grpc</c> assembly (T-28, WO-041 P-255) — now that
/// both cross-domain blockers (<c>01.Core</c> P-249 <see cref="LoggingEventIdRanges"/> and
/// <c>00.Governance</c> P-250 <see cref="LoggingEventIdIntegrityAssertion"/>) have shipped. Mirrors the
/// established real-assembly invocation pattern from <c>05.Application.Behaviors.Tests</c> and
/// <c>SharedKernel.Messaging.MassTransit.Tests</c>.
/// </summary>
/// <remarks>
/// <c>SharedKernel.Communication.Grpc</c> reserves <c>11100-11199</c> within the domain's
/// <c>11000-11999</c> block (<see cref="LoggingEventIdRanges.Communication"/>), populated today by the
/// two P-255 <c>[LoggerMessage]</c> methods (11100, 11101).
/// </remarks>
public sealed class LoggingEventIdIntegrityRealAssemblyTests
{
    [Fact]
    public void AssertGloballyUniqueAndInRange_RealGrpcAssembly_Passes()
    {
        var grpcAssembly = typeof(CorrelationTracingInterceptor).Assembly;

        var domainRange = (LoggingEventIdRanges.Communication, LoggingEventIdRanges.Communication + 999);

        var assemblyRanges = new Dictionary<Assembly, (int RangeMin, int RangeMax)>
        {
            [grpcAssembly] = domainRange,
        };

        var act = () => LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(assemblyRanges);

        act.Should().NotThrow(
            "every [LoggerMessage] EventId in SharedKernel.Communication.Grpc must be globally unique "
            + "and fall within the 11000-11999 11.Communication domain range reserved by 01.Core's "
            + "LoggingEventIdRanges.Communication registry");
    }

    [Fact]
    public void AssertGloballyUniqueAndInRange_GrpcAndInternalAssembliesTogether_NoCrossAssemblyCollisions()
    {
        // Verifies EventId 11100/11101 (Grpc) and 11300-11308 (Internal) never collide with each other
        // when both assemblies are checked in the same aggregate pass — the "no cross-assembly collisions"
        // clause of T-28. Lives in Grpc.Tests (not Internal.Tests) because SharedKernel.Communication.Grpc's
        // production .csproj already legitimately references SharedKernel.Communication.Internal (G-09
        // service-discovery integration) — the reverse direction is a layering violation
        // (an undeclared Adapter -> Adapter edge, SKTIER002, and a reference cycle).
        var grpcAssembly = typeof(CorrelationTracingInterceptor).Assembly;
        var internalAssembly = typeof(IServiceEndpointResolver).Assembly;

        var domainRange = (LoggingEventIdRanges.Communication, LoggingEventIdRanges.Communication + 999);

        var assemblyRanges = new Dictionary<Assembly, (int RangeMin, int RangeMax)>
        {
            [grpcAssembly] = domainRange,
            [internalAssembly] = domainRange,
        };

        var act = () => LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(assemblyRanges);

        act.Should().NotThrow(
            "SharedKernel.Communication.Grpc and SharedKernel.Communication.Internal share the same "
            + "11000-11999 domain range but must never collide on an individual EventId");
    }
}
