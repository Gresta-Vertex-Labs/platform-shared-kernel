namespace SharedKernel.ServiceDefaults.Tests.Telemetry.GrpcFixtures;

/// <summary>
/// Forces the response's reported <see cref="HttpResponseMessage.Version"/> to match the request's.
/// </summary>
/// <remarks>
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>'s in-memory
/// <c>TestServer</c> transport does not itself report an HTTP/2 response version, which
/// <c>Grpc.Net.Client</c> validates before accepting a response. This is the standard, documented
/// workaround (Microsoft's "Test gRPC services in ASP.NET Core" guidance) for exercising a real
/// gRPC call against an in-memory host in a unit test.
/// </remarks>
internal sealed class ResponseVersionHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        response.Version = request.Version;
        return response;
    }
}
