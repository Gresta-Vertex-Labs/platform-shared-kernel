# SharedKernel.Presentation.Core

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **What every inbound boundary shares — HTTP, gRPC and SignalR: declarative authorization attributes over
> `IUserContext`, and one set of error rules, so an error or a refusal reads the same on every protocol and a gRPC host
> needs no HTTP API stack.**

| You get | So that |
| --- | --- |
| `[RequireEndpointPermission]`, `[RequireRole]` | Endpoints, hubs and gRPC methods that send no command are gated on the platform's permission and role model |
| `[RequireFreshAuthentication]`, `[RequireAuthenticationMethod]` | Sensitive operations demand a recent sign-in or a second factor, with an RFC 9470 step-up challenge |
| Matching endpoint conventions (`.RequireEndpointPermission(…)`, …) | Route groups, `MapHub` and `MapGrpcService` get the same gates |
| Real `[Authorize]` attributes over native policies | They work wherever ASP.NET Core authorizes — minimal APIs, MVC, hubs, gRPC |
| One `ErrorType` → status map and client-message rule | A failure is 404 over REST, `NotFound` over gRPC and the same code on a hub, with server errors redacted outside Development |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

Services get this package with `SharedKernel.Presentation.WebApi`, `.Grpc` or `.SignalR`. Reference it directly only
in a project that declares the attributes without hosting any of those (a shared controllers library, for example):

```xml
<PackageReference Include="SharedKernel.Presentation.Core" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** project |
| Depends on | `SharedKernel.Primitives`, `SharedKernel.Execution`, `SharedKernel.Localization`, `SharedKernel.Security.Abstractions`, ASP.NET Core |
| Namespaces | `SharedKernel.Presentation.Authorization` |

## Quick start

Nothing to register: `AddSharedKernelWebApi()`, `AddSharedKernelGrpc()` and `AddSharedKernelSignalR()` register the
authorization this package provides.

```csharp
using SharedKernel.Presentation.Authorization;

// An endpoint that sends no command (a report download):
app.MapGet("/reports/{id}", GetReport).RequireEndpointPermission("reports.read");

// A hub:
[RequireRole("support")]
public sealed class SupportHub : Hub { }

// A gRPC method that must have been signed in within the last five minutes with a second factor:
[RequireAuthenticationMethod("otp", MaxAgeSeconds = 300)]
public override Task<Reply> CloseAccount(CloseRequest request, ServerCallContext context) => ...;
```

## How it works

| Attribute / convention | Checks, through the caller's `IUserContext` | Refused with |
| --- | --- | --- |
| `[RequireEndpointPermission(params string[])]` / `.RequireEndpointPermission(…)` | `HasPermission` (ordinal), any listed | 401 anonymous, 403 `forbidden.insufficient_permission` |
| `[RequireRole(params string[])]` / `.RequireRole(…)` | `HasRole` (ordinal), any listed | 401, 403 `forbidden.insufficient_permission` |
| `[RequireFreshAuthentication(int maxAgeSeconds)]` / `.RequireFreshAuthentication(int or TimeSpan)` | `IsAuthenticationFresherThan(maxAge, IClock.UtcNow)` | 401 step-up, `unauthorized.step_up_required` |
| `[RequireAuthenticationMethod(params string[])]` (`MaxAgeSeconds`) / `.RequireAuthenticationMethod([TimeSpan,] …)` | `WasAuthenticatedWith`, and with a maximum age `GetAuthenticationMethodTime` | 401 step-up, with `max_age` when set |

- **Composition.** Values within one attribute are alternatives (OR); several attributes must all be satisfied (AND).
  An anonymous caller is always challenged (401) first.
- **Refusals.** Not signed in: the scheme's own challenge and 401 `unauthorized.default` (`WWW-Authenticate: Bearer`
  when no scheme is registered). Signed in but only freshness or method requirements unmet: 401 with an RFC 9470
  `insufficient_user_authentication` challenge (`DPoP` or `Bearer`, plus the smallest `max_age`). Otherwise 403 — the
  message never names the roles or permissions required. `SharedKernel.Presentation.WebApi` adds the RFC 9457 body; a
  gRPC call gets no body (401 → `Unauthenticated`, 403 → `PermissionDenied`).
- **The requirement is in the policy name** and cannot be replaced: `Policy` and `Roles` are read-only;
  `AuthenticationSchemes` can be set as on `[Authorize]`.
- **Mappers.** Every authentication scheme needs an `IUserContextMapper` (the SharedKernel OIDC, API-key and mTLS
  packages register one). A principal no mapper understands is refused with 403, and each scheme without a mapper is
  named in a warning at startup.
- **Error rules** (internal, applied by WebApi, Grpc and SignalR): `ErrorType` → HTTP status is Validation 400,
  Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409, BusinessRule 422, Unexpected 500, Unavailable 503,
  Timeout 504, anything else 500 (WebApi adds 412 for a version conflict on a conditional request; gRPC has its own
  sibling map). The client message is the error's message, translated through an optional `ILocalizationCatalog` in
  the request culture — or, for a 5xx outside Development, a generic sentence. The correlation id every protocol
  reports is the request's `RequestContextScope` id, from `UseSharedKernelRequestContext()`.

## Reference

### Public API

| Member | Namespace `SharedKernel.Presentation.Authorization` |
| --- | --- |
| `RequireEndpointPermissionAttribute(params string[] permissions)` | `Permissions` |
| `RequireRoleAttribute(params string[] roles)` | `Roles` |
| `RequireFreshAuthenticationAttribute(int maxAgeSeconds)` | `MaxAge` |
| `RequireAuthenticationMethodAttribute(params string[] methods)` | `Methods`, `MaxAgeSeconds` |
| `AuthorizationConventionExtensions` | `RequireEndpointPermission`, `RequireRole`, `RequireFreshAuthentication` (seconds or `TimeSpan`), `RequireAuthenticationMethod` (with or without a `TimeSpan` maximum age) on any `IEndpointConventionBuilder` |

### Errors

| Code | Status | When |
| --- | --- | --- |
| `unauthorized.default` | 401 | Anonymous caller |
| `unauthorized.step_up_required` | 401 | Signed in, but not recently enough or not with the required method |
| `forbidden.insufficient_permission` | 403 | Signed in without the permission or role |

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 14002 | Warning | Authorization refused — endpoint and code, never principal data |
| 14009 | Warning | A principal no `IUserContextMapper` understands |
| 14010 | Warning | An authentication scheme without a mapper (at startup) |

## Testing

Drive the attributes through a `WebApplicationFactory<Program>` host with a test authentication scheme, or register
[`SharedKernel.Security.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Security.Testing/README.md)'s
`FakeUserContext` with the `Permissions`, `Roles`, `AuthenticationMethods` and `AuthTime` the case needs. For gRPC
methods called directly, [`SharedKernel.Presentation.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Presentation.Testing/README.md)'s
`TestServerCallContext.Create(...)` supplies a `ServerCallContext`.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Repeat a command's permission on the endpoint | Put `[RequirePermission]` on the command (`05.Application`) | It is enforced on every path — HTTP, messages, jobs, workflows |
| Use `[Authorize(Roles = "...")]` | Use `[RequireRole]` | ASP.NET Core's roles read claims directly and bypass the mapper |
| Register an authentication scheme without an `IUserContextMapper` | Use a SharedKernel scheme or register a mapper | Its callers are refused with 403 |
| Name the missing permission in a custom 403 message | Keep the platform's refusal | The message must not disclose the authorization model |
| Put freshness checks in the handler | Use `[RequireFreshAuthentication]` / `[RequireAuthenticationMethod]` at the edge | Step-up is an authentication concern with a standard challenge |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Presentation domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
