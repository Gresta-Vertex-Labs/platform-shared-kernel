using System.Reflection;
using SharedKernel.ArchitectureTests;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Communication.Grpc.Tests.Governance;

/// <summary>
/// Runs <c>00.Governance</c>'s <see cref="LoggingEventIdIntegrityAssertion"/> over the real, compiled assemblies:
/// <c>SharedKernel.Communication</c> (11000–11099) and <c>SharedKernel.Communication.Grpc</c> (11100–11199) share the
/// domain's 11000–11999 block and must never collide.
/// </summary>
public sealed class LoggingEventIdIntegrityRealAssemblyTests
{
    [Fact]
    public void The_grpc_and_core_assemblies_have_unique_event_ids_in_the_communication_range()
    {
        var range = (LoggingEventIdRanges.Communication, LoggingEventIdRanges.Communication + 999);
        var assemblies = new Dictionary<Assembly, (int RangeMin, int RangeMax)>
        {
            [typeof(GrpcClientOptions).Assembly] = range,
            [typeof(CommunicationOptions).Assembly] = range,
        };

        var check = () => LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(assemblies);

        check.Should().NotThrow();
    }
}
