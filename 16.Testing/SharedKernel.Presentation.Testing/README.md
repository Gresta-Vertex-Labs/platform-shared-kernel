# SharedKernel.Presentation.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**Test helpers for the inbound API boundary: a gRPC `ServerCallContext` that carries an `HttpContext` for server
interceptor tests, a HotChocolate request executor with test-safe defaults, and a fixed `IHttpContextAccessor`.**
For ASP.NET Core endpoint and middleware tests use `WebApplicationFactory` as usual; these helpers cover the
pieces it makes awkward.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Presentation.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespaces: `SharedKernel.Testing.Grpc` and
`SharedKernel.Testing.Communication`.

## Contents

| Type | Namespace | What it does |
| --- | --- | --- |
| `TestServerCallContext.Create(correlationId, requestHeaders, configureServices, endpointMetadata, httpContext, method, host, deadline, cancellationToken)` | `SharedKernel.Testing.Grpc` | A gRPC `ServerCallContext` whose `GetHttpContext()` returns a real `HttpContext`: its request services come from `configureServices`, its endpoint carries `endpointMetadata` (for `[RequireRole]`/`[RequirePermission]`/`[RequireFreshAuthentication]`), and `correlationId` becomes the correlation header |
| `GraphQLTestExecutorFactory.Create(services)` | `SharedKernel.Testing.Communication` | `AddGraphQLServer()` with introspection on and `MaxPageSize = 10`, for isolated schema tests. The platform's own conventions are tested in `SharedKernel.Presentation.GraphQL`'s suite, not through this |
| `FakeHttpContextAccessor` | `SharedKernel.Testing.Communication` | A fixed (or `null`) `HttpContext`; `FakeHttpContextAccessor.WithTenant(tenantId)` builds one whose request services resolve an `IRequestContext` for that tenant |

The core package's `SharedKernel.Testing.Communication` holds the HTTP client doubles too; the namespace is shared,
the packages are not. A plain `ServerCallContext` without `HttpContext` is in
[`SharedKernel.Communication.Testing`](../SharedKernel.Communication.Testing/README.md).

## Example

```csharp
var context = TestServerCallContext.Create(
    correlationId: "abc-123",
    configureServices: s => s.AddSingleton<IUserContext>(new SecurityTestContextBuilder().WithRoles("admin").BuildUserContext()),
    endpointMetadata: [new RequireRoleAttribute("admin")]);

var response = await interceptor.UnaryServerHandler(request, context, (req, ctx) => Task.FromResult(new Reply()));
```

## Related packages

- References `SharedKernel.Execution`, `SharedKernel.Presentation.Grpc` (and through it ASP.NET Core), HotChocolate
  and `Grpc.Core.Testing`.
- [`SharedKernel.Security.Testing`](../SharedKernel.Security.Testing/README.md) — `FakeUserContext`,
  `SecurityTestContextBuilder`.
