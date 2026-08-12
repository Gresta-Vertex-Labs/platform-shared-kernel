// consumer-verify — exercises every 11.Communication production package exactly as a downstream
// microservice would: real DI composition through ProjectReference (standing in for a packed NuGet
// reference — the compiled surface is identical either way), driven through a real
// Host.CreateApplicationBuilder() -> IHost.StartAsync() composition, never a bare
// BuildServiceProvider() alone — mirroring the 17.Workflows/consumer-verify precedent's shape
// (PB-07/P-362, WO-056). Three surfaces:
//   1. All four DI entry points (AddSharedKernelRestCommunication().AddRestClient<T>(),
//      AddSharedKernelGrpcCommunication().AddGrpcClient<T>(), AddSharedKernelGraphQL(),
//      AddK8sServiceDiscovery()) resolve cleanly with zero DI exceptions through a real
//      IHost.StartAsync() — including K8sServiceDiscoveryOptions' genuine ValidateOnStart() path.
//   2. A deliberately invalid RestClientOptions (TimeoutSeconds = -1) causes AddRestClient<TClient>
//      to throw OptionsValidationException synchronously at registration time — proving P-358's
//      R-23 fix end-to-end against real compiled code, not a unit test calling the validator
//      object directly.
//   3. A deliberately invalid GraphQLOptions (MaxPageSize = 501) causes AddSharedKernelGraphQL to
//      throw OptionsValidationException synchronously at registration time — proving P-358's
//      GQ-10 fix end-to-end, the same way.
//
// Note on Surface 1's REST/gRPC client registrations: both SampleRestClient and SampleGrpcClient are
// registered with an explicit BaseAddress/Address rather than relying on the AddK8sServiceDiscovery
// resolver registered alongside them in the same host — this proves each of the four DI entry points
// independently rather than coupling REST/gRPC address resolution to service-discovery DNS I/O (which
// would make this harness's pass/fail depend on the runtime environment's DNS/network availability).
// AddK8sServiceDiscovery's own contract — IServiceEndpointResolver.ResolveAsync never throws for an
// unresolvable name — is proven directly, by resolving IServiceEndpointResolver from the host and
// calling ResolveAsync on a deliberately nonexistent service name.

using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.GraphQL.Extensions;
using SharedKernel.Communication.Grpc.Extensions;
using SharedKernel.Communication.Internal.Extensions;
using SharedKernel.Communication.Internal.Resolvers;
using SharedKernel.Communication.Rest.Builders;
using SharedKernel.Communication.Rest.Extensions;

await Surface1_AllFourDiEntryPointsResolveCleanly();
Surface2_InvalidRestClientOptionsThrowsAtRegistrationTime();
Surface3_InvalidGraphQLOptionsThrowsAtRegistrationTime();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");
return;

// ── Surface 1: all four DI entry points — P-362 ──────────────────────────────
static async Task Surface1_AllFourDiEntryPointsResolveCleanly()
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();

    builder.Services
        .AddSharedKernelRestCommunication()
        .AddRestClient<SampleRestClient>(
            "sample-rest-client",
            options => options.BaseAddress = "http://sample-rest-service");

    builder.Services
        .AddSharedKernelGrpcCommunication()
        .AddGrpcClient<SampleGrpcClient>(address: "http://sample-grpc-service:5001");

    var graphQlBuilder = builder.Services.AddSharedKernelGraphQL();
    Verify(graphQlBuilder is not null, "AddSharedKernelGraphQL() returns a non-null IRequestExecutorBuilder");

    builder.Services.AddK8sServiceDiscovery(options => options.Namespace = "consumer-verify");

    using IHost host = builder.Build();
    // Exercises the real ValidateOnStart() path (K8sServiceDiscoveryOptionsValidator) through a
    // genuine IHost, not just BuildServiceProvider().
    await host.StartAsync();

    SampleRestClient restClient = host.Services.GetRequiredService<SampleRestClient>();
    Verify(restClient is not null, "SampleRestClient resolves through AddRestClient<T>() with zero DI exceptions");

    SampleGrpcClient grpcClient = host.Services.GetRequiredService<SampleGrpcClient>();
    Verify(grpcClient is not null, "SampleGrpcClient resolves through AddGrpcClient<T>() with zero DI exceptions");

    IServiceEndpointResolver resolver = host.Services.GetRequiredService<IServiceEndpointResolver>();
    Uri resolved = await resolver.ResolveAsync("nonexistent-consumer-verify-service", CancellationToken.None);
    Verify(
        resolved is not null,
        "IServiceEndpointResolver.ResolveAsync never throws and returns a Uri for an unresolvable service name");

    await host.StopAsync();
    Console.WriteLine(
        "Surface 1 PASS: AddSharedKernelRestCommunication/AddSharedKernelGrpcCommunication/" +
        "AddSharedKernelGraphQL/AddK8sServiceDiscovery all resolve cleanly through a real " +
        "IHost.StartAsync() with zero DI exceptions.");
}

// ── Surface 2: invalid RestClientOptions — P-358/R-23 ────────────────────────
static void Surface2_InvalidRestClientOptionsThrowsAtRegistrationTime()
{
    var services = new ServiceCollection();
    IRestCommunicationBuilder restBuilder = services.AddSharedKernelRestCommunication();

    OptionsValidationException? caught = null;
    try
    {
        restBuilder.AddRestClient<SampleRestClient>(
            "invalid-rest-client",
            options =>
            {
                options.BaseAddress = "http://sample-rest-service";
                options.TimeoutSeconds = -1;
            });
    }
    catch (OptionsValidationException ex)
    {
        caught = ex;
    }

    Verify(
        caught is not null,
        "AddRestClient<T> throws OptionsValidationException synchronously at registration time for TimeoutSeconds = -1");
    Verify(
        caught!.Failures.Any(failure => failure.Contains("TimeoutSeconds", StringComparison.Ordinal)),
        "the OptionsValidationException names the invalid TimeoutSeconds property");

    Console.WriteLine(
        "Surface 2 PASS: an invalid RestClientOptions instance fails AddRestClient<T> loudly at " +
        "registration time — not deferred to the first HTTP call — proving P-358's R-23 fix end-to-end " +
        "against real compiled code.");
}

// ── Surface 3: invalid GraphQLOptions — P-358/GQ-10 ──────────────────────────
static void Surface3_InvalidGraphQLOptionsThrowsAtRegistrationTime()
{
    var services = new ServiceCollection();

    OptionsValidationException? caught = null;
    try
    {
        services.AddSharedKernelGraphQL(options => options.MaxPageSize = 501);
    }
    catch (OptionsValidationException ex)
    {
        caught = ex;
    }

    Verify(
        caught is not null,
        "AddSharedKernelGraphQL throws OptionsValidationException synchronously at registration time for MaxPageSize = 501");
    Verify(
        caught!.Failures.Any(failure => failure.Contains("MaxPageSize", StringComparison.Ordinal)),
        "the OptionsValidationException names the invalid MaxPageSize property");

    Console.WriteLine(
        "Surface 3 PASS: an invalid GraphQLOptions instance fails AddSharedKernelGraphQL loudly at " +
        "registration time — before any HotChocolate schema is built — proving P-358's GQ-10 fix " +
        "end-to-end against real compiled code.");
}

static void Verify(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {label}");
    }

    Console.WriteLine($"  - {label}");
}

// A minimal typed REST client — satisfies AddHttpClient<TClient>()'s constructor convention
// (a public constructor accepting HttpClient). No real HTTP call is ever issued by this harness.
internal sealed class SampleRestClient
{
    public SampleRestClient(HttpClient httpClient)
    {
    }
}

// A minimal typed gRPC client — Grpc.Net.ClientFactory's DefaultClientActivator<T> specifically
// looks for a CallInvoker-accepting constructor when activating a typed client through DI (a
// ChannelBase-accepting constructor is rejected). No real RPC call is ever issued by this harness.
internal sealed class SampleGrpcClient
{
    public SampleGrpcClient(CallInvoker callInvoker)
    {
    }
}
