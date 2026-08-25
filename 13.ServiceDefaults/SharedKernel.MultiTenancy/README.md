# SharedKernel.MultiTenancy

Concrete multi-tenant resolution strategies for Platform.SharedKernel microservices. Resolves the ambient tenant identity for each request and exposes it through `ITenantProvider`.

## Included

**`TenantResolutionMiddleware`** — runs the configured strategies in order; the first one returning a non-null tenant wins.

| Strategy | Resolves from |
|---|---|
| `ClaimTenantResolutionStrategy` | A signature-verified JWT tenant claim |
| `HeaderTenantResolutionStrategy` | The `X-Tenant-Id` request header |
| `DatabaseTenantResolutionStrategy` | A tenant-directory lookup (host/domain → tenant) |

**`AmbientTenantProvider`** — the resolved identity, injected as `ITenantProvider`.

**`ITenantStatusValidator`** — optional seam. When registered, it is consulted after resolution so a suspended or offboarded tenant is rejected even if it presents an otherwise valid claim or header. Bridge it to your own tenant directory at the composition root.

**`ITenantResolutionStrategy`** — implement this to add your own strategy.

**`TenantResolutionOptions`** — bound from configuration section `SharedKernel:MultiTenancy`, validated at startup by `TenantResolutionOptionsValidator` (a typo in `StrategyOrder` fails the host rather than silently resolving nothing).

## Quick Start

```csharp
// Register (Program.cs)
builder.Services.AddSharedKernelMultiTenancy(builder.Configuration);

app.UseMiddleware<TenantResolutionMiddleware>();

// Consume
public class OrderService(ITenantProvider tenants)
{
    public Guid CurrentTenant => tenants.TenantId;
}
```

```json
{
  "SharedKernel": {
    "MultiTenancy": {
      "StrategyOrder": [ "Claim", "Header", "Database" ]
    }
  }
}
```

## Security note — strategy order matters

The default order is **`Claim` → `Header` → `Database`**, and it is deliberate.

A JWT tenant claim is signature-verified; the `X-Tenant-Id` header is caller-supplied and trivially forged. Placing `Header` ahead of `Claim` lets any caller override a verified identity with an arbitrary one — a cross-tenant impersonation vector. This ordering is locked by an architecture test in `00.Governance` so it cannot silently regress.

A service with no tenant directory simply omits `"Database"` from the order — no code change required.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [13.ServiceDefaults README](../README.md) for the full host-composition layer.
