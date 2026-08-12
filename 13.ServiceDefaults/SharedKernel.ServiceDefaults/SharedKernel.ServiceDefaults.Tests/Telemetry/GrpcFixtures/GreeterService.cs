using Grpc.Core;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry.GrpcFixtures;

/// <summary>
/// Trivial server implementation of the test-only <c>Greeter</c> service (see
/// <c>greeter.proto</c>) — exists solely so T-43's genuine gRPC capture tests have a real RPC to
/// call. No production behavior.
/// </summary>
internal sealed class GreeterService : Greeter.GreeterBase
{
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context) =>
        Task.FromResult(new HelloReply { Message = $"Hello {request.Name}" });
}
