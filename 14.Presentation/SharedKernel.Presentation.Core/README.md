# SharedKernel.Presentation.Core

What the SharedKernel inbound API boundaries share, so every protocol answers the same way and a gRPC host needs no
HTTP API stack: the declarative authorization attributes, the authorization policies behind them, and the error rules
(status category and client message) that HTTP problems, SignalR hub errors and gRPC statuses all apply.

**Tier:** Host. It references `SharedKernel.Primitives`, `SharedKernel.Execution`, `SharedKernel.Localization`,
`SharedKernel.Security.Abstractions` and the ASP.NET Core shared framework; no third-party packages.
[`SharedKernel.Presentation.WebApi`](../SharedKernel.Presentation.WebApi/README.md),
[`SharedKernel.Presentation.Grpc`](../SharedKernel.Presentation.Grpc/README.md),
[`SharedKernel.Presentation.SignalR`](../SharedKernel.Presentation.SignalR/README.md) and
[`SharedKernel.Presentation.OpenApi`](../SharedKernel.Presentation.OpenApi/README.md) reference it, so gRPC never
references WebApi (P-570, completed by P-579).

---

## Installation

Services get this package with WebApi, Grpc or SignalR. Reference it directly only in a project that declares the
attributes without hosting any of them (for example a shared controllers library):

```xml
<PackageReference Include="SharedKernel.Presentation.Core" />
```

Versions come from your single `SharedKernelVersion`; every SharedKernel package ships with the same version. There is
nothing to register: `AddSharedKernelWebApi()`, `AddSharedKernelGrpc()` and `AddSharedKernelSignalR()` register the
authorization this package provides.

---

## Authorization attributes — `SharedKernel.Presentation.Authorization`

```csharp
using SharedKernel.Presentation.Authorization;
```

| Attribute / convention | Checks, through the caller's `IUserContext` | Refused with |
| --- | --- | --- |
| `[RequireEndpointPermission(params string[] permissions)]`, `.RequireEndpointPermission(…)` | `HasPermission` (ordinal), any listed | 401 anonymous, 403 `forbidden.insufficient_permission` |
| `[RequireRole(params string[] roles)]`, `.RequireRole(…)` | `HasRole` (ordinal), any listed | 401, 403 |
| `[RequireFreshAuthentication(int maxAgeSeconds)]`, `.RequireFreshAuthentication(…)` | `IsAuthenticationFresherThan(maxAge, IClock.UtcNow)` | 401 with an RFC 9470 step-up challenge, `unauthorized.step_up_required` |
| `[RequireAuthenticationMethod(params string[] methods)]` (optional `MaxAgeSeconds`), `.RequireAuthenticationMethod(…)` | `WasAuthenticatedWith`, and with a maximum age `GetAuthenticationMethodTime` | 401 step-up, with `max_age` when set |

- They are real ASP.NET Core `[Authorize]` attributes backed by native policies, so they work wherever ASP.NET Core
  authorizes: minimal APIs and groups, MVC, SignalR hubs and hub methods, gRPC services and methods, and
  `MapHub`/`MapGrpcService` conventions. Values within one attribute are alternatives (OR); several attributes must all
  be satisfied (AND). An anonymous caller is always challenged (401) first.
- The requirement is encoded in the policy name and cannot be replaced: `Policy` and `Roles` are read-only.
  `AuthenticationSchemes` can be set as on `[Authorize]`.
- **Permissions of a use case go on the command or query** (`05.Application`'s `[RequirePermission]`), not on the
  endpoint. `[RequireEndpointPermission]` is for what sends no command: hubs, gRPC methods, endpoints that do not call
  `ISender`, the OpenAPI documents. Authentication strength stays at the edge.
- Every authentication scheme needs an `IUserContextMapper` (the SharedKernel OIDC, API key and mTLS packages register
  one); a caller no mapper understands is refused with 403, and each such scheme is named in a warning at startup.
- Never use `[Authorize(Roles = "...")]` alongside these: it reads role claims directly and bypasses the mapper.

A refused HTTP request gets its status and challenge headers here; `SharedKernel.Presentation.WebApi` adds the RFC 9457
problem body. A gRPC call never gets a body: gRPC answers 401 as `Unauthenticated` and 403 as `PermissionDenied`.

---

## Error rules (internal)

Services never call these directly; the protocol packages do, so one error reads the same everywhere:

- `ErrorType` to HTTP status: Validation 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409,
  BusinessRule 422, Unexpected 500, Unavailable 503, Timeout 504, anything else 500 (gRPC's own map,
  `GrpcStatusCodeMap`, is in the Grpc package). WebApi adds the one request-dependent rule: a version conflict of a
  conditional request is 412.
- The client message: the error's message, translated through an optional `ILocalizationCatalog` in the request's
  culture; for a server error (a 5xx status) outside Development, a generic sentence instead, because such messages
  describe internals.
- The correlation id every protocol reports (problem `correlationId`, gRPC `ErrorInfo`, SignalR) is the one held by the
  request's `RequestContextScope`, which `SharedKernel.ServiceDefaults.Security`'s `UseSharedKernelRequestContext()`
  opens.

---

## Logging

This package emits three events of the `14000–14099` sub-block, with the numbers they had in WebApi before P-579:
14002 Warning (authorization refused: endpoint and code, never principal data), 14009 Warning (a principal no
`IUserContextMapper` understands), 14010 Warning (an authentication scheme without a mapper, at startup).

---

## Related packages

- [`SharedKernel.Presentation.WebApi`](../SharedKernel.Presentation.WebApi/README.md) — the HTTP boundary; problem bodies.
- [`SharedKernel.Presentation.Grpc`](../SharedKernel.Presentation.Grpc/README.md) — the rich gRPC status.
- [`SharedKernel.Presentation.SignalR`](../SharedKernel.Presentation.SignalR/README.md) — coded hub errors.
- `SharedKernel.Security.Abstractions` (`12.Security`) — `IUserContext`, `IUserContextMapper`.
- `SharedKernel.ServiceDefaults.Security` (`13.ServiceDefaults`) — the request context and its correlation id.
