# SharedKernel.Communication.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Test doubles for a service's own REST and gRPC clients: a stub server that runs a typed client's whole pipeline
> with no network, fake unary calls for a mocked gRPC client, and a gRPC `ServerCallContext`.** Test retries,
> idempotency keys, caller headers and error mapping exactly as they run in production. To test one
> `DelegatingHandler` on its own, `FakeHttpMessageHandler` in `SharedKernel.Testing` is the lighter fit; to test a
> server-side gRPC method that reads `HttpContext`, use `SharedKernel.Presentation.Testing`.

| You get | So that |
| --- | --- |
| `StubHttpMessageHandler` | Canned answers by method and path — JSON, the platform's ProblemDetails, a bare status, a thrown failure, a slow reply |
| `services.UseStubHttpMessageHandler(clientName, stub)` | Every handler of an `AddRestClient` client still runs; only the network is replaced |
| `stub.Requests` (`RecordedHttpRequest`) | Each attempt is recorded with its method, address, headers and body, so retries and propagated headers are assertable |
| `GrpcCalls.Success` / `Failure` | A mocked generated client returns what a platform service would, including the rich status `ToResultAsync()` reads |
| `GrpcCalls.ToRpcException(error)` | A test gRPC service throws exactly the exception a platform service's error arrives as |
| `TestServerCallContext.Create(...)` | A `ServerCallContext` for a service method or server interceptor, without a channel |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

Add it to a **test project** only — never to production code. `TestingNeverReferencedByProduction` (an architecture
rule you can run against your own assemblies) fails any production project that references a testing package.

```xml
<PackageReference Include="SharedKernel.Communication.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Communication`, `SharedKernel.Primitives`, `Microsoft.Extensions.Http`, `Grpc.Core.Api`, `Grpc.Core.Testing`, `Grpc.StatusProto`, `Google.Api.CommonProtos` |
| Namespaces | `SharedKernel.Testing.Communication` |

Your test project also references the client packages under test — `SharedKernel.Communication.Rest` for
`AddRestClient`, `SharedKernel.Communication.Grpc` for `ToResultAsync()`.

## Quick start

```csharp
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Communication;
using Xunit;

public sealed record StockLevel(string Sku, int Available);

public sealed class InventoryClient(HttpClient http)
{
    public Task<Result<StockLevel>> GetStockAsync(string sku, CancellationToken ct) =>
        http.GetResultAsync<StockLevel>($"stock/{sku}", ct);
}

public sealed class InventoryClientTests
{
    [Fact]
    public async Task A_missing_sku_reads_back_as_the_services_error()
    {
        var stub = new StubHttpMessageHandler()
            .RespondJson(HttpMethod.Get, "/stock/sku-1", new StockLevel("sku-1", 3))
            .RespondProblem(HttpMethod.Get, "/stock/sku-9", HttpStatusCode.NotFound, "inventory.sku_not_found");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Communication:Clients:inventory:BaseAddress"] = "http://inventory",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCommunication(configuration).AddRestClient<InventoryClient>("inventory");
        services.UseStubHttpMessageHandler("inventory", stub);   // after AddRestClient
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<InventoryClient>();

        Result<StockLevel> found = await client.GetStockAsync("sku-1", CancellationToken.None);
        Result<StockLevel> missing = await client.GetStockAsync("sku-9", CancellationToken.None);

        Assert.Equal(3, found.Value.Available);
        Assert.Equal(ErrorType.NotFound, missing.Error.Type);
        Assert.Equal("inventory.sku_not_found", missing.Error.Code);
        Assert.NotNull(stub.Requests[0].Header("X-Correlation-Id"));   // the client's own handlers ran
    }
}
```

## How it works

- **Routing.** A route matches the request's method and path, whatever the host — service discovery may have
  rewritten it. A route whose path contains `?` must match path and query; otherwise the query is ignored. Paths
  compare case-insensitively and a missing leading `/` is added. The route added **last** wins.
- **Unanswered requests fail the test:** the handler throws `InvalidOperationException` naming the method and path.
- **Fresh answers per attempt.** Each response is built when the request arrives, so a retry gets a new response;
  `Respond`/`RespondAsync` receive the number of earlier requests to that route, to answer attempts differently.
  `RespondAsync` receives the request's cancellation token, which a timeout or a winning hedged attempt cancels.
- **Recording.** `Requests` is every request in arrival order, each retry attempt separately. Headers are request
  headers only (not content headers), each value comma-joined; `Header(name)` compares names case-insensitively.
- **Under a typed client.** `UseStubHttpMessageHandler` makes the stub the client's primary handler, so every
  delegating handler still runs (caller headers, `Idempotency-Key`, resilience, credentials). The stub is wrapped per
  handler rotation and never disposed by the `IHttpClientFactory`, so one stub serves the whole test.
- **Thread-safe:** routes and recordings tolerate concurrent (hedged or parallel) requests.
- **gRPC failures.** `GrpcCalls.Failure<T>(error)` builds the rich status a platform service sends: the gRPC status
  of the `ErrorType` (the same forward map as `SharedKernel.Presentation.Grpc`), an `ErrorInfo` with the error code
  (domain `GrpcCalls.ErrorDomain`, `"sharedkernel.testing"`) and, for `error.Details`, a `BadRequest` of field
  violations. `Failure<T>(statusCode, detail)` is a bare status, as a non-platform service or the gRPC client itself
  would produce.

| `ErrorType` | gRPC status |
| --- | --- |
| `Validation` | `InvalidArgument` |
| `Unauthorized` | `Unauthenticated` |
| `Forbidden` | `PermissionDenied` |
| `NotFound` | `NotFound` |
| `Conflict` | `Aborted` |
| `BusinessRule` | `FailedPrecondition` |
| `Unavailable` | `Unavailable` |
| `Timeout` | `DeadlineExceeded` |
| `Unexpected` | `Internal` |

## Recipes

### 1. Prove a POST is retried with the same idempotency key

```csharp
var stub = new StubHttpMessageHandler()
    .Respond(HttpMethod.Post, "/reservations", (_, attempt) =>
        new HttpResponseMessage(attempt == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created));

// … register the client with PropagateIdempotencyKey on, call it …

Assert.Equal(2, stub.Requests.Count);
Assert.Single(stub.Requests.Select(r => r.Header("Idempotency-Key")).Distinct());
```

### 2. Return field errors

```csharp
stub.RespondProblem(
    HttpMethod.Post, "/reservations", HttpStatusCode.BadRequest, "validation.failed",
    fieldErrors: new Dictionary<string, string[]> { ["quantity"] = ["Must be positive."] });
```

The client's `Result` is an `ErrorType.Validation` error whose `Details` carry one entry per field.

### 3. Simulate an unreachable or slow service

```csharp
stub.Throw(HttpMethod.Get, "/stock/sku-1", new HttpRequestException("Connection refused"));   // communication.unreachable

stub.RespondAsync(HttpMethod.Get, "/stock/sku-2", async (_, _, ct) =>
{
    await Task.Delay(TimeSpan.FromSeconds(5), ct);   // cancelled by the client's timeout
    return new HttpResponseMessage(HttpStatusCode.OK);
});
```

### 4. Mock a generated gRPC client

```csharp
using Grpc.Core;
using NSubstitute;
using SharedKernel.Communication;                  // ToResultAsync()
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Communication;

var client = Substitute.For<Inventory.InventoryClient>();   // generated methods are virtual; any mocking library works
client.GetStockAsync(Arg.Any<GetStockRequest>(), Arg.Any<CallOptions>())
    .Returns(GrpcCalls.Failure<StockReply>(Error.NotFound("inventory.sku_not_found", "No such SKU.")));

Result<StockReply> result = await client.GetStockAsync(new GetStockRequest(), new CallOptions()).ToResultAsync();
Assert.Equal("inventory.sku_not_found", result.Error.Code);
```

### 5. Call a gRPC service method directly

```csharp
ServerCallContext context = TestServerCallContext.Create(
    requestHeaders: new Metadata { { "x-custom", "value" } },
    method: "/inventory.Inventory/GetStock",
    deadline: DateTime.UtcNow.AddSeconds(5));

StockReply reply = await service.GetStock(new GetStockRequest { Sku = "sku-1" }, context);
```

This context has no `HttpContext`. For a server-side method that calls `context.GetHttpContext()`, use
`SharedKernel.Testing.Grpc.TestServerCallContext` from
[`SharedKernel.Presentation.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/SharedKernel.Presentation.Testing/README.md).

## Reference

### Registration

| Method | Does |
| --- | --- |
| `UseStubHttpMessageHandler(this IServiceCollection services, string clientName, StubHttpMessageHandler stub)` | Makes `stub` the primary handler of the named client. Call it after `AddRestClient` |

### `StubHttpMessageHandler`

| Member | Answers with |
| --- | --- |
| `RespondJson<T>(HttpMethod method, string path, T body, HttpStatusCode status = OK)` | `body` as JSON (`JsonSerializerOptions.Web`, camelCase) |
| `RespondProblem(HttpMethod method, string path, HttpStatusCode status, string errorCode, string? detail = null, IReadOnlyDictionary<string, string[]>? fieldErrors = null)` | An `application/problem+json` body: `type`, `title`, `status`, `detail` (the reason phrase when `null`), `errorCode`, and `errors` for field errors |
| `RespondStatus(HttpMethod method, string path, HttpStatusCode status)` | The status, no body |
| `Throw(HttpMethod method, string path, Exception exception)` | Throws `exception` — an `HttpRequestException` for an unreachable service |
| `Respond(HttpMethod method, string path, Func<HttpRequestMessage, int, HttpResponseMessage> respond)` | Whatever `respond` builds from the request and the earlier-hit count |
| `RespondAsync(HttpMethod method, string path, Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond)` | The same, asynchronously, with the cancellation token |
| `static Problem(HttpStatusCode status, string errorCode, string? detail = null, IReadOnlyDictionary<string, string[]>? fieldErrors = null)` | The response `RespondProblem` sends, for use inside `Respond` |
| `Requests` | `IReadOnlyList<RecordedHttpRequest>` — `Method`, `Uri`, `Headers`, `Body`, `Header(name)` |

Every `Respond*`/`Throw` returns the stub, for chaining.

### `GrpcCalls` and `TestServerCallContext`

| Member | Returns |
| --- | --- |
| `GrpcCalls.Success<TResponse>(TResponse response)` | `AsyncUnaryCall<TResponse>` completing with `response` and status OK |
| `GrpcCalls.Failure<TResponse>(Error error)` | `AsyncUnaryCall<TResponse>` failing with the rich status of `error` |
| `GrpcCalls.Failure<TResponse>(StatusCode statusCode, string detail = "")` | `AsyncUnaryCall<TResponse>` failing with a bare status |
| `GrpcCalls.ToRpcException(Error error)` | The `RpcException`, status details in its trailers |
| `GrpcCalls.ErrorDomain` | `"sharedkernel.testing"`, the `ErrorInfo` domain |
| `TestServerCallContext.Create(Metadata? requestHeaders = null, string method = "test-method", string host = "localhost", DateTime? deadline = null, CancellationToken cancellationToken = default)` | A `ServerCallContext` (peer `test-peer`, no deadline by default) built on `Grpc.Core.Testing` |

## Testing

This package is the test double; its self-tests live in
[`SharedKernel.Communication.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/src/Infrastructure/Communication/SharedKernel.Communication.Testing/SharedKernel.Communication.Testing.Tests),
which run the stub under a real `AddRestClient` client and read the gRPC fakes back through the real
`ToResultAsync()`. Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md):
`TestRequestContext` inside a `RequestContextScope` to assert the tenant and caller headers a client propagates,
`ActivityRecorder` for client spans, and `FakeHttpMessageHandler`/`HttpClientHandlerTestFactory` to test a single
`DelegatingHandler` in isolation.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Call `UseStubHttpMessageHandler` before `AddRestClient` | Register the client first, then the stub | The stub must replace the primary handler of the client as registered |
| Wrap the stub in `new HttpClient(stub)` to test a typed client | Use `UseStubHttpMessageHandler` | A bare `HttpClient` skips the client's handlers — no headers, idempotency key or retries to assert |
| Reuse one `Respond(...)` answer for "first fails, then succeeds" by adding two routes | Branch on the attempt number inside one `Respond` | The route added last always wins; earlier routes for the same path are shadowed |
| Leave a request unstubbed and expect a 404 | Stub every path the test calls | An unmatched request throws `InvalidOperationException` so a wrong URL fails loudly |
| Assert the exact `ErrorInfo` domain of a failure | Assert the `Error.Code` and `Error.Type` after `ToResultAsync()` | The fake uses `"sharedkernel.testing"`, not your service's domain |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Communication packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Communication/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
