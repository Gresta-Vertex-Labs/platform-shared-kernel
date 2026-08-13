# 12.Security

Identity and access abstractions for Platform.SharedKernel microservices — the current user's identity, the current tenant, and the machinery to authenticate a request via JWT/OIDC or a pre-shared API key.

Philosophy: **Thin abstractions. Claims-first. No domain coupling. Request-scoped identity.**

- **`SharedKernel.Security.Abstractions`** — `IUserContext`, `ITenantProvider`, `IdentityKind`, `AnonymousUserContext`/`SystemUserContext` sentinels, `SecurityClaimTypes`. Zero NuGet dependencies — the only types application and domain-adjacent code should ever inject.
- **`SharedKernel.Security.Oidc`** — the concrete JWT/OIDC implementation: `OidcUserContext`/`OidcTenantProvider`, configurable claims-to-context mapping, Azure B2C / Microsoft Entra External ID wiring, structured security-audit logging, and the `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` DI entry points.
- **`SharedKernel.Security.ApiKey`** — pre-shared-key / machine-client authentication. A sibling provider to `.Oidc`, never a dependent of it — composes alongside JWT Bearer via a policy/forwarding scheme so a host can accept either credential type on the same endpoints.

Every microservice in the platform depends on `SharedKernel.Security.Abstractions` to read the current user and tenant — never on `.Oidc` or `.ApiKey` directly. Only the host's composition root (`Program.cs`) references the concrete provider packages.

## Quick Start

```csharp
// Standard JWT / Microsoft Entra ID (non-B2C):
services.AddSharedKernelSecurity(configuration);

// Azure B2C / Microsoft Entra External ID:
services.AddAzureB2CAuthentication(configuration);

// Machine-client / pre-shared-key callers — composes alongside either call above:
services.AddApiKeyAuthentication<MyDatabaseBackedApiKeyValidator>();

// In application code, inject the abstractions — never a concrete OIDC/ApiKey type:
public sealed class MyCommandHandler(IUserContext user, ITenantProvider tenant)
{
    public Task Handle(MyCommand command, CancellationToken ct)
    {
        if (!user.IsAuthenticated)
        {
            return Task.FromException(new UnauthorizedAccessException());
        }

        var tenantId = tenant.TenantId; // pass as a Guid primitive to domain constructors
        // ...
    }
}
```

`IUserContext` and `ITenantProvider` are both **scoped** — one instance per HTTP request. Both are always resolvable: `AnonymousUserContext` is the registered fallback when no HTTP context is present (background workers, console hosts, unit-test DI containers). Background-execution hosts (Temporal activities, MassTransit consumers, hosted services) register `SystemUserContext` explicitly in place of the HTTP-derived factory — see the "Background-execution host" recipe in `SharedKernel.Security.Oidc/README.md`.

## Ordering rules

1. Call `AddSharedKernelSecurity`/`AddAzureB2CAuthentication` first — it registers JWT Bearer authentication plus the scoped `IUserContext`/`ITenantProvider` factories.
2. `AddApiKeyAuthentication<TValidator>()` is optional and additive. Call it *after* step 1 so its scheme-aware `IUserContext` factory can correctly delegate to the OIDC-backed factory for non-API-key-authenticated requests.
3. Application code injects `IUserContext`/`ITenantProvider` — never `IHttpContextAccessor`, `ClaimsPrincipal`, or `HttpContext` directly. Those are infrastructure details this domain exists to hide.

## Packages

| Package | README |
| --- | --- |
| `SharedKernel.Security.Abstractions` | [`SharedKernel.Security.Abstractions/README.md`](SharedKernel.Security.Abstractions/README.md) |
| `SharedKernel.Security.Oidc` | [`SharedKernel.Security.Oidc/README.md`](SharedKernel.Security.Oidc/README.md) — also holds the four end-to-end cross-domain recipes (`05.Application`, `06.Persistence`, `07.Messaging`, background-execution hosts) |
| `SharedKernel.Security.ApiKey` | [`SharedKernel.Security.ApiKey/README.md`](SharedKernel.Security.ApiKey/README.md) |

See `12.Security/CLAUDE.md` for the full interface contracts, implementation rules, and AOT notes.
