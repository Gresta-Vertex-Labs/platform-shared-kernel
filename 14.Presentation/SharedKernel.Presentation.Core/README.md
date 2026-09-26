# SharedKernel.Presentation.Core

What the HTTP and gRPC boundary packages share: the declarative authorization attributes and the
`ErrorType` status maps. It has no ASP.NET Core reference.

**Tier:** Host. It references `SharedKernel.Primitives` and `Grpc.Core.Api` (for `StatusCode`) only.
[`SharedKernel.Presentation.WebApi`](../SharedKernel.Presentation.WebApi/README.md) and
[`SharedKernel.Presentation.Grpc`](../SharedKernel.Presentation.Grpc/README.md) both reference it, so
neither boundary package references the other and a service has one authorization dialect.

---

## Installation

Services normally get this package transitively through `SharedKernel.Presentation.WebApi` or
`SharedKernel.Presentation.Grpc`. Reference it directly only in a project that declares the
attributes without hosting either boundary (for example a shared controllers library):

```xml
<PackageReference Include="SharedKernel.Presentation.Core" />
```

Versions come from your single `SharedKernelVersion`; every SharedKernel package ships with the
same version. There is nothing to register: the package holds attributes and static helpers only.

---

## Authorization attributes — `SharedKernel.Presentation.Authorization`

| Attribute | Checks, through `IUserContext` | Composition |
| --- | --- | --- |
| `[RequireRole(params string[] roles)]` | `HasRole` (ordinal) | any listed role; stacked attributes AND |
| `[RequirePermission(params string[] permissions)]` | `HasPermission` (ordinal) | any listed permission; stacked attributes AND |
| `[RequireFreshAuthentication(int maxAgeSeconds)]` | `IsAuthenticationFresherThan(maxAge, IClock.UtcNow)` | one per target; `maxAgeSeconds` > 0 |
| `[RequireAuthenticationMethod(params string[] methods)]` | `WasAuthenticatedWith` | any listed `amr` value |

All four compose AND across each other. A failed check is `Error.Forbidden` — HTTP 403
`ProblemDetails` or gRPC `PermissionDenied`. An anonymous caller fails through the ordinary false
path. The attributes do nothing on their own; the evaluator is WebApi's
`AuthorizationRequirementEndpointFilter` (attach it with `.AddEndpointFilter<…>()`) or Grpc's
`GrpcAuthorizationInterceptor` (registered globally by `AddSharedKernelGrpc()`).

```csharp
using SharedKernel.Presentation.Authorization;

[RequireRole("Admin", "Auditor")]            // Admin OR Auditor ...
[RequirePermission("reports:approve")]       // ... AND reports:approve
[RequireFreshAuthentication(300)]            // ... AND authenticated in the last 5 minutes
public sealed class ReportApprovalsController : ControllerBase { /* ... */ }
```

Never use ASP.NET Core's `[Authorize(Roles = "...")]` alongside these: it reads `ClaimTypes.Role`
directly and bypasses the authentication package's `IUserContextMapper`.

---

## Status maps — `SharedKernel.Presentation.Errors`

Two single sources of truth; never duplicate either switch in a service.

| `ErrorType` | `ErrorTypeStatusCodeMap.Resolve` (HTTP) | `GrpcStatusCodeMap.Resolve` (gRPC) |
| --- | --- | --- |
| `Validation` | 400 | `InvalidArgument` |
| `Unauthorized` | 401 | `Unauthenticated` |
| `Forbidden` | 403 | `PermissionDenied` |
| `NotFound` | 404 | `NotFound` |
| `Conflict` | 409 | `Aborted` |
| `BusinessRule` | 422 | `FailedPrecondition` |
| `Unexpected` | 500 | `Internal` |
| anything else, including `None` | 500 | `Unknown` |

`ErrorTypeStatusCodeMap` returns an `int` built from `System.Net.HttpStatusCode`. Services rarely call
either map directly: `Error.ToProblemDetails()`/`ResultHttpExtensions` (WebApi) and
`GrpcResultExtensions`/`GrpcExceptionInterceptor` (Grpc) call them.

---

## Related packages

- [`SharedKernel.Presentation.WebApi`](../SharedKernel.Presentation.WebApi/README.md) — evaluates the attributes on HTTP endpoints and maps `Error` to `ProblemDetails`.
- [`SharedKernel.Presentation.Grpc`](../SharedKernel.Presentation.Grpc/README.md) — evaluates them on gRPC service methods and maps `Error` to `RpcException`.
- `SharedKernel.Security.Abstractions` — `IUserContext`, which the evaluators read.
