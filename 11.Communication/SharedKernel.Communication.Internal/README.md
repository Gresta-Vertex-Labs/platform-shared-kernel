# SharedKernel.Communication.Internal

K8s-native service discovery for Platform.SharedKernel microservices: `IServiceEndpointResolver`,
`KubernetesServiceEndpointResolver` (DNS SRV + A-record via `Microsoft.Extensions.ServiceDiscovery`,
with a TTL-based in-memory endpoint cache and stale-while-revalidate fallback),
`StaticServiceEndpointResolver` (dev/test only — logs `Warning` at startup), and the
`AddK8sServiceDiscovery`/`AddStaticServiceDiscovery` DI extensions.

`ResolveAsync` **never throws** for an unresolvable service name — it returns the K8s DNS-convention
`Uri` (`{scheme}://{serviceName}.{namespace}.svc.{clusterDomain}`) and lets the caller's transport
(REST, gRPC) surface the connection error.

## Install

```bash
dotnet add package SharedKernel.Communication.Internal
```

```xml
<PackageReference Include="SharedKernel.Communication.Internal" Version="1.0.0" />
```

## Usage

```csharp
// Production — K8s in-cluster DNS resolution
services.AddK8sServiceDiscovery(options =>
{
    options.Namespace = "production";
    options.ClusterDomain = "cluster.local";
    options.EndpointCacheTtlSeconds = 30;  // 0 disables caching entirely
});

// Inject IServiceEndpointResolver
Uri endpoint = await resolver.ResolveAsync("order-service", ct);
// → http://order-service.production.svc.cluster.local (on DNS resolution or on fallback — never throws)
```

```csharp
// Local dev / tests only — a fixed endpoint map, never used in production
services.AddStaticServiceDiscovery(new Dictionary<string, Uri>
{
    ["order-service"]   = new Uri("http://localhost:5001"),
    ["payment-service"] = new Uri("http://localhost:5002"),
});
// Logs Warning at startup. Throws InvalidOperationException if IServiceEndpointResolver is already
// registered — the guard is symmetric in both call orders (AddK8sServiceDiscovery ⇄
// AddStaticServiceDiscovery).
```

`SharedKernel.Communication.Rest`'s `AddRestClient<TClient>` and
`SharedKernel.Communication.Grpc`'s `AddGrpcClient<TClient>` both consume whichever
`IServiceEndpointResolver` is registered here automatically — omit `BaseAddress`/`Address` on the
typed-client options to opt into resolver-driven address resolution instead of a hardcoded value.

## Recipe: only one resolver may be registered at a time

`AddK8sServiceDiscovery` and `AddStaticServiceDiscovery` both throw `InvalidOperationException` when
`IServiceEndpointResolver` is already registered — whichever call runs second fails loudly instead of
silently losing the intended resolver:

```csharp
services.AddK8sServiceDiscovery();
services.AddStaticServiceDiscovery(new Dictionary<string, Uri>());
// ^ throws InvalidOperationException — call exactly one of these per service, never both.
```

## Layering

```text
SharedKernel.Communication.Internal  →  SharedKernel.Primitives (01.Core),
                                         Microsoft.Extensions.ServiceDiscovery
```

Target framework: `net10.0`. No `04.Contracts`, `12.Security`, or higher-numbered domain reference —
this is the foundational, dependency-lightest package in `11.Communication`, consumed intra-domain by
both `.Rest` and `.Grpc`.

For full documentation see
[`11.Communication/CLAUDE.md`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/11.Communication/CLAUDE.md).
