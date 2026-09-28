# SharedKernel.Communication.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**Test helpers for a service's own REST and gRPC clients:** a stub server that runs a typed client's whole pipeline
without a network, fake gRPC calls for a mocked generated client, and a gRPC `ServerCallContext`.

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
| `StubHttpMessageHandler` | Answers by method and path, whatever the host: `RespondJson`, `RespondProblem` (the platform's RFC 9457 problem: `errorCode`, `detail`, field `errors`), `RespondStatus`, `Throw` (an unreachable service), `Respond`/`RespondAsync` (per attempt, or slow). The route added last wins; an unanswered request fails the test. `Requests` records each attempt as sent — method, address, headers, body |
| `services.UseStubHttpMessageHandler(clientName, stub)` | Puts the stub under a client registered with `AddRestClient`: every handler of the client still runs (caller headers, `Idempotency-Key`, retries, credentials, service discovery) and the result helpers read its answers |
| `GrpcCalls.Success(response)` / `Failure<T>(error)` / `Failure<T>(statusCode, detail)` | The `AsyncUnaryCall<T>` a generated client's method returns, for a mocked client. `Failure(error)` carries the rich status a platform service sends — `ErrorInfo` with the code, `BadRequest` with the field errors — so `ToResultAsync()` reads the same `Error` back. `GrpcCalls.ToRpcException(error)` gives the exception itself, to throw from a test gRPC service |
| `TestServerCallContext.Create(requestHeaders, method, host, deadline, cancellationToken)` | A gRPC `ServerCallContext` built on `Grpc.Core.Testing`, for a service method or server interceptor without a channel |

## Examples

A typed client against canned answers:

```csharp
var stub = new StubHttpMessageHandler()
    .RespondJson(HttpMethod.Get, "/stock/sku-1", new StockLevel("sku-1", 3))
    .Respond(HttpMethod.Post, "/reservations", (_, attempt) =>
        new HttpResponseMessage(attempt == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created));

services.AddSharedKernelCommunication(configuration).AddRestClient<IInventoryClient, InventoryClient>("inventory");
services.UseStubHttpMessageHandler("inventory", stub);

// ...

stub.Requests.Should().HaveCount(3);
stub.Requests.Select(r => r.Header("Idempotency-Key")).Distinct().Should().ContainSingle("a retry repeats the key");
```

A mocked gRPC client (NSubstitute; the generated methods are virtual):

```csharp
var client = Substitute.For<Inventory.InventoryClient>();
client.GetStockAsync(Arg.Any<GetStockRequest>(), Arg.Any<CallOptions>())
    .Returns(GrpcCalls.Failure<StockReply>(Error.NotFound("inventory.sku_not_found", "No such SKU.")));
```

`StubHttpMessageHandler` is for a client registered with `AddRestClient`. To test a single `DelegatingHandler` in
isolation, the core package's `FakeHttpMessageHandler` and `HttpClientHandlerTestFactory` are enough. A server-side
context that also carries an `HttpContext` is in [`SharedKernel.Presentation.Testing`](../SharedKernel.Presentation.Testing/README.md).

## Related packages

- References `SharedKernel.Communication`, `SharedKernel.Primitives`, `Microsoft.Extensions.Http`, `Grpc.Core.Api`,
  `Grpc.Core.Testing`, `Grpc.StatusProto` and `Google.Api.CommonProtos`.
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — `FakeHttpMessageHandler`, `HttpClientHandlerTestFactory`,
  `ActivityRecorder`, `TestRequestContext`.
