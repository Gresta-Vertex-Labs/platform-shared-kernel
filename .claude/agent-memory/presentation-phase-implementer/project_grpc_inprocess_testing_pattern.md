---
name: project_grpc_inprocess_testing_pattern
description: Established, working pattern for testing a real gRPC-over-HTTP2 server in-process (no sockets, no Docker) via WebApplicationFactory + a hand-written .proto — used by both 13.ServiceDefaults.Tests and SharedKernel.Presentation.Grpc.Tests.
type: project
---

Two independent packages now use the identical pattern to host a real gRPC server in a unit-test
process with no real network/Kestrel socket: `13.ServiceDefaults/.../Telemetry/GrpcFixtures/`
(T-43/WO-056, `greeter.proto`) and `14.Presentation/SharedKernel.Presentation.Grpc.Tests/Integration/`
(T-70-T-75/WO-074, `test.proto`). Treat this as the canonical shape for any future gRPC-hosting
test need on this platform — do not reinvent it.

**Package wiring (no new `Directory.Packages.props` entry needed if `Grpc.AspNetCore` is already
pinned):**
```xml
<PackageReference Include="Grpc.AspNetCore" />   <!-- pulls Grpc.Tools transitively, include="All" per its .nuspec -->
<PackageReference Include="Grpc.Net.Client" />   <!-- the client channel -->
<ItemGroup>
  <Protobuf Include="Integration\Protos\test.proto" GrpcServices="Both" />
</ItemGroup>
```
Confirmed via direct `.nuspec` inspection: `Grpc.AspNetCore`'s dependency group for every recent
TFM (`net8.0`/`net9.0`/`net10.0`) includes `<dependency id="Grpc.Tools" version="2.80.0"
include="All" />` — `Grpc.Tools`' MSBuild targets (the `.proto` → C# codegen) come along for free
with no separate `PackageReference`/`PackageVersion`.

**Fixture trio (mirror these three files verbatim, renaming types):**
1. A trivial `TestServiceImpl : TestService.TestServiceBase` overriding the RPCs the tests need.
2. `GrpcTestWebApplicationFactory : WebApplicationFactory<GrpcTestWebApplicationFactory>` —
   overrides `CreateHostBuilder()` to return `Host.CreateDefaultBuilder().ConfigureWebHostDefaults
   (webBuilder => webBuilder.UseEnvironment(Environments.Development).UseStartup<Startup>())`
   (explicitly pin the environment — overriding `CreateHostBuilder` bypasses
   `WebApplicationFactory`'s own default environment wiring, so an unset `ASPNETCORE_ENVIRONMENT`
   in a CI/test shell silently falls back to the BCL's "Production" default instead of
   `WebApplicationFactory`'s usual "Development" default — this cost a real failing-test debugging
   round trip this session). Also overrides `CreateHost(IHostBuilder)` to call
   `builder.UseContentRoot(AppContext.BaseDirectory)` — `WebApplicationFactory`'s content-root
   heuristic fails for a marker type nested under a non-root-adjacent domain folder; the actual
   value doesn't matter beyond "a directory that exists." The nested `Startup` class wires
   `services.AddGrpc()`/`AddSharedKernelGrpc()` + `app.UseRouting()` +
   `app.UseEndpoints(e => e.MapGrpcService<TestServiceImpl>())`.
3. `ResponseVersionHandler : DelegatingHandler` — forces `response.Version = request.Version`.
   `WebApplicationFactory`'s in-memory `TestServer` transport does not itself report an HTTP/2
   response version, which `Grpc.Net.Client` validates before accepting a response; without this
   handler every call throws at the client. This is Microsoft's own documented workaround for
   "Test gRPC services in ASP.NET Core," not a local hack.

**Building the client:**
```csharp
var httpClient = factory.CreateDefaultClient(new ResponseVersionHandler());
var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
var client = new TestService.TestServiceClient(channel);
```

**Per-scenario customization** (different `IUserContext`/`IRequestContext`/`IClock`/environment
per test): `factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
services.AddSingleton<IUserContext>(new FakeUserContext { Roles = ["admin"] })))` — last
registration wins for single-instance DI resolution, so this cleanly overrides the base `Startup`'s
default fakes without needing a separate factory subclass per scenario. `.RemoveAll<T>()`
(`Microsoft.Extensions.DependencyInjection.Extensions`) proves a "no-op when the attribute/feature
is absent" contract by removing a service entirely rather than leaving a default fake in place.

**Testing a real outbound client against this same in-memory host:** don't build a raw
`GrpcChannel` — if the interceptor pipeline under test is a real "sanctioned DI builder" one (e.g.
`SharedKernel.Communication.Grpc`'s `AddSharedKernelGrpcCommunication().AddGrpcClient<TClient>()`),
route ITS underlying named `HttpClient` through the same TestServer instead of hand-rolling
metadata: `services.AddHttpClient(typeof(TClient).Name).AddHttpMessageHandler(() => new
ResponseVersionHandler()).ConfigurePrimaryHttpMessageHandler(() =>
factory.Server.CreateHandler())` — `Grpc.Net.ClientFactory`'s `AddGrpcClient<TClient>()` registers
a named `HttpClient` keyed by the client type's simple name, and named-client configuration from
multiple `AddHttpClient(name)` calls composes additively, so this genuinely exercises the real
interceptors end-to-end rather than a stand-in.
