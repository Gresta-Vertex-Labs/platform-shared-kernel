# SharedKernel.Presentation.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Test helpers for the inbound API boundary: a gRPC `ServerCallContext` that carries a real `HttpContext`, a
> HotChocolate request executor with small paging defaults, and a fixed `IHttpContextAccessor`.** Unit-test gRPC
> service methods, your own server interceptors and GraphQL schemas without standing up a host. For the client side
> of a gRPC call, use `SharedKernel.Communication.Testing` instead.

| You get | So that |
| --- | --- |
| `TestServerCallContext.Create(...)` (`SharedKernel.Testing.Grpc`) | `context.GetHttpContext()` returns an `HttpContext` with your request services and endpoint metadata, as in a host |
| `correlationId:` argument | The call arrives with the inbound `X-Correlation-Id` header a client would send |
| `GraphQLTestExecutorFactory.Create(services)` | A HotChocolate server with cursor paging (page size 10, total count) to add your types to |
| `FakeHttpContextAccessor` / `.WithTenant(tenantId)` | Code that reads `IHttpContextAccessor` gets a fixed context — or none — with an `IRequestContext` for a tenant |

For ASP.NET Core endpoints and middleware, use `WebApplicationFactory` as usual; these helpers cover what it makes
awkward.

## Install

Add it to a **test project** only — never to production code. `TestingNeverReferencedByProduction` (an architecture
rule you can run against your own assemblies) fails any production project that references a testing package.

```xml
<PackageReference Include="SharedKernel.Presentation.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Execution`, `SharedKernel.Presentation.Grpc` (and through it ASP.NET Core and `SharedKernel.Presentation.Core`), `Grpc.Core.Api`, `Grpc.Core.Testing`, `HotChocolate.AspNetCore`, `HotChocolate.Data`, `Microsoft.Extensions.DependencyInjection` |
| Namespaces | `SharedKernel.Testing.Grpc` (`TestServerCallContext`), `SharedKernel.Testing.Communication` (`GraphQLTestExecutorFactory`, `FakeHttpContextAccessor`) |

The snippets below also use `TestRequestContext` (`SharedKernel.Testing.Execution`), from
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md);
reference it too.

## Quick start

A gRPC service method that reads the caller from its request services and ends a failed `Result` with
`GetValueOrThrow()`, tested without a host:

```csharp
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Core.Extensions;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Execution;
using SharedKernel.Testing.Grpc;
using Xunit;

// The code under test, written as a hosted gRPC method would be.
public static class OrderLookup
{
    public static Task<string> GetAsync(string id, ServerCallContext context)
    {
        var caller = context.GetHttpContext().RequestServices.GetRequiredService<IRequestContext>();
        Result<string> result = id == "7" && caller.TenantId is not null
            ? Result<string>.Success($"order-{id}")
            : Result<string>.Failure(Error.NotFound("order.not_found", $"Order {id} was not found."));

        return Task.FromResult(result.GetValueOrThrow());
    }
}

public sealed class OrderLookupTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());

    [Fact]
    public async Task Unknown_order_throws_the_exception_of_its_error()
    {
        ServerCallContext context = TestServerCallContext.Create(
            correlationId: "corr-42",
            configureServices: s => s.AddSingleton<IRequestContext>(TestRequestContext.ForTenant(Tenant)));

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => OrderLookup.GetAsync("42", context));

        Assert.Equal("order.not_found", exception.Error.Code);
    }
}
```

## How it works

- **The `HttpContext` bridge.** Hosted by ASP.NET Core, a gRPC method reaches the request through
  `context.GetHttpContext()`. `Create` stores its `HttpContext` under the `UserState` key that call reads, so code
  under test resolves `RequestServices` and the endpoint's metadata as it would in a host. Without `httpContext:`,
  a `DefaultHttpContext` is built whose `RequestServices` come from `configureServices` and whose endpoint carries
  `endpointMetadata`; pass `httpContext:` for full control (the other two are then ignored).
- **What it does not simulate.** Calls in a host run through the HTTP pipeline: `UseSharedKernelRequestContext()`
  opens the call's `RequestContextScope`, ASP.NET Core authorization enforces `[RequireEndpointPermission]` and its
  siblings, and `SharedKernel.Presentation.Grpc`'s exception interceptor turns exceptions into the rich
  `google.rpc.Status`. A hand-built context passes through none of them:
  - open `RequestContextScope.Begin(...)` yourself when the code reads `IRequestContextAccessor.Current`;
  - endpoint metadata you attach is readable but **not enforced** — test authorization against an in-process host;
  - a failed `Result` ended with `GetValueOrThrow()`/`ThrowIfFailure()` surfaces as `Error.ToException()` (for
    example `NotFoundException`), not as an `RpcException`.
- **No default caller.** `Create` registers no `IRequestContext` or `IUserContext`; supply the ones the code needs
  through `configureServices` (`TestRequestContext`, `FakeUserContext`).
- **Other defaults:** method `test-method`, host `localhost`, peer `test-peer`, no deadline (`DateTime.MaxValue`),
  `CancellationToken.None`. After the call, inspect `context.ResponseTrailers` and `context.Status`.
- **GraphQL.** `GraphQLTestExecutorFactory.Create` calls HotChocolate's `AddGraphQLServer()` with the queryable cursor
  paging provider and `MaxPageSize = 10`, `DefaultPageSize = 10`, `IncludeTotalCount = true`. It does **not** apply
  `AddSharedKernelGraphQL()`'s conventions (snake_case filtering, the error filter); those are verified by
  `SharedKernel.Presentation.GraphQL`'s own suite.
- **`FakeHttpContextAccessor.WithTenant(tenantId)`** builds a `DefaultHttpContext` whose request services resolve a
  `SystemRequestContext` (no permissions, identity `test`) for the tenant — `null` for no tenant.

## Recipes

### 1. Test a service's own server interceptor

```csharp
ServerCallContext context = TestServerCallContext.Create(
    requestHeaders: new Metadata { { "x-client-version", "2.4.0" } },
    method: "/orders.Orders/Get",
    configureServices: s => s.AddSingleton<IRequestContext>(TestRequestContext.ForTenant(tenant)));

var response = await interceptor.UnaryServerHandler(
    new GetOrderRequest { Id = "7" },
    context,
    (request, ctx) => Task.FromResult(new GetOrderReply()));
```

### 2. Read endpoint metadata the method inspects

```csharp
ServerCallContext context = TestServerCallContext.Create(endpointMetadata: [new AuditedOperationAttribute("orders.read")]);

var metadata = context.GetHttpContext().GetEndpoint()!.Metadata.GetMetadata<AuditedOperationAttribute>();
```

`AuditedOperationAttribute` stands for any marker of your own. Authorization attributes placed here are not enforced.

### 3. Run code that reads the ambient caller

```csharp
var caller = TestRequestContext.ForTenant(tenant);
ServerCallContext context = TestServerCallContext.Create(configureServices: s => s.AddSingleton<IRequestContext>(caller));

using (RequestContextScope.Begin(caller))   // what UseSharedKernelRequestContext() does in a host
{
    await service.Get(new GetOrderRequest { Id = "7" }, context);
}
```

### 4. Execute a GraphQL query in memory

```csharp
using HotChocolate.Execution;

var services = new ServiceCollection();
IRequestExecutor executor = await GraphQLTestExecutorFactory.Create(services)
    .AddQueryType<ProductQuery>()
    .BuildRequestExecutorAsync();

IExecutionResult result = await executor.ExecuteAsync("{ products(first: 5) { nodes { name } totalCount } }");
```

`ProductQuery.GetProducts()` returns an `IQueryable<Product>` marked `[UsePaging]`; the factory's paging defaults then
cap it at 10 items per page.

### 5. Give a component an `IHttpContextAccessor`

```csharp
var withTenant = FakeHttpContextAccessor.WithTenant(tenant);   // request services resolve IRequestContext
var noRequest = new FakeHttpContextAccessor();                  // HttpContext is null: outside a request
var custom = new FakeHttpContextAccessor(new DefaultHttpContext());
```

## Reference

| Member | Returns / does |
| --- | --- |
| `TestServerCallContext.Create(string? correlationId = null, Metadata? requestHeaders = null, Action<IServiceCollection>? configureServices = null, IEnumerable<object>? endpointMetadata = null, HttpContext? httpContext = null, string method = "test-method", string host = "localhost", DateTime? deadline = null, CancellationToken cancellationToken = default)` | A `ServerCallContext` built on `Grpc.Core.Testing` whose `GetHttpContext()` returns the supplied or built `HttpContext`; `correlationId` is added to the request headers as `WellKnownHeaders.CorrelationId` |
| `GraphQLTestExecutorFactory.Create(IServiceCollection services)` | The `IRequestExecutorBuilder` from `AddGraphQLServer()`, with cursor paging configured as above; add your types to it |
| `new FakeHttpContextAccessor(HttpContext? context = null)` | `IHttpContextAccessor` holding `context`; `HttpContext` is settable |
| `FakeHttpContextAccessor.WithTenant(TenantId? tenantId)` | An accessor whose context's request services resolve an `IRequestContext` for `tenantId` |

The helpers return no `Error` codes and register nothing in your container; they are plain factories.

## Testing

This package is the test helper; nothing tests it from your side. Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
for `TestRequestContext`, and with
[`SharedKernel.Security.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Security/SharedKernel.Security.Testing/README.md)
for `FakeUserContext`. A client-side `ServerCallContext` without an `HttpContext` is in
[`SharedKernel.Communication.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Communication/SharedKernel.Communication.Testing/README.md).

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Test `[RequireEndpointPermission]`, `[RequireRole]` or step-up attributes through `endpointMetadata` | Test authorization against an in-process host (`WebApplicationFactory`) | ASP.NET Core authorization enforces them in the pipeline; a hand-built context never passes through it |
| Expect an `RpcException` with a rich status from a failed `Result` | Assert on the `SharedKernelException` subtype and its `Error` | Only the Presentation.Grpc exception interceptor, in a host, converts it |
| Assume `IRequestContextAccessor.Current` is set because you passed `correlationId` | Open `RequestContextScope.Begin(...)` around the call | `correlationId` is only the inbound header; nothing opens the scope for you |
| Import both `SharedKernel.Testing.Grpc` and `SharedKernel.Testing.Communication` with `Communication.Testing` also referenced | Alias one (`using CallContext = SharedKernel.Testing.Grpc.TestServerCallContext;`) | Both packages have a `TestServerCallContext`, and the names collide |
| Verify platform GraphQL conventions with `GraphQLTestExecutorFactory` | Call `AddSharedKernelGraphQL()` in that test instead | The factory configures raw HotChocolate, not the platform conventions |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Presentation packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
