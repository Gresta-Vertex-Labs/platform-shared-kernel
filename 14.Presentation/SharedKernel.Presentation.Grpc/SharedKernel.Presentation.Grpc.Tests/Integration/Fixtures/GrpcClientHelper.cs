using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;

/// <summary>
/// Builds a real <see cref="GrpcChannel"/>-backed <see cref="TestService.TestServiceClient"/>
/// against a <see cref="GrpcTestWebApplicationFactory"/>'s in-memory <c>TestServer</c>.
/// </summary>
internal static class GrpcClientHelper
{
    public static TestService.TestServiceClient CreateClient(WebApplicationFactory<GrpcTestWebApplicationFactory> factory)
    {
        var httpClient = factory.CreateDefaultClient(new ResponseVersionHandler());
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
        return new TestService.TestServiceClient(channel);
    }
}
