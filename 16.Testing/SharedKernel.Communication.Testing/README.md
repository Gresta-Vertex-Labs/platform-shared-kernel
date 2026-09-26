# SharedKernel.Communication.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**Test helpers for the SharedKernel outbound communication packages: a configurable `IServiceEndpointResolver`
and a gRPC `ServerCallContext` for interceptor tests.** For HTTP client pipelines use `FakeHttpMessageHandler` and
`HttpClientHandlerTestFactory` from the core [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) package,
which need no communication package at all.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Communication.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Communication`.

## Contents

| Type | What it does |
| --- | --- |
| `MockServiceEndpointResolver` | An `IServiceEndpointResolver` with fixed answers: `Configure(serviceName, uri)`; an unconfigured name falls back to `http://{name}.default.svc.cluster.local`, never throwing, like the production resolver; `GetResolvedNames()` records every lookup in order |
| `TestServerCallContext.Create(requestHeaders, method, host, deadline, cancellationToken)` | A gRPC `ServerCallContext` built on `Grpc.Core.Testing`, for exercising a client or server interceptor without a channel |

There is no `Add*` helper: register the resolver as you would any instance —
`services.AddSingleton<IServiceEndpointResolver>(resolver)`.

## Example

```csharp
var resolver = new MockServiceEndpointResolver();
resolver.Configure("billing", new Uri("http://billing.test"));
var client = new BillingClient(new HttpClient(handler), resolver);

await client.GetInvoiceAsync(invoiceId, ct);

resolver.GetResolvedNames().Should().Equal("billing");
```

```csharp
var context = TestServerCallContext.Create(
    requestHeaders: new Metadata { { WellKnownHeaders.CorrelationId, "abc-123" } });
```

A server-side context that also carries an `HttpContext` (services, endpoint metadata) is in
[`SharedKernel.Presentation.Testing`](../SharedKernel.Presentation.Testing/README.md), under
`SharedKernel.Testing.Grpc`.

## Related packages

- References `SharedKernel.Communication.Internal`, `Grpc.Core.Api` and `Grpc.Core.Testing`.
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — `FakeHttpMessageHandler`, `HttpClientHandlerTestFactory`,
  `ActivityRecorder`, `AmbientActivityTestHelper`.
