# SharedKernel.Security.Abstractions

Zero-dependency security abstractions for the SharedKernel. References only `SharedKernel.Primitives`.

## Public Surface

| Type | Kind | Purpose |
|------|------|---------|
| `IUserContext` | Interface | Current user identity — UserId, Email, Username, Roles, Claims, IsAuthenticated, HasRole |
| `ITenantProvider` | Interface | Current tenant identity — TenantId (Guid.Empty when absent) |
| `AnonymousUserContext` | Sealed class | Sentinel for unauthenticated requests; always resolvable from DI |
| `SecurityClaimTypes` | Static class | Well-known claim type string constants (sub, tenant_id, email, role) |

## Usage

```csharp
// Application layer
public class MyCommandHandler(IUserContext user, ITenantProvider tenant)
{
    public Task Handle(MyCommand cmd)
    {
        if (!user.IsAuthenticated) throw new UnauthorizedAccessException();
        var tenantId = tenant.TenantId; // pass as Guid to domain constructor
    }
}
```

Both `IUserContext` and `ITenantProvider` are scoped — one instance per HTTP request. Register via `AddSharedKernelSecurity` from `SharedKernel.Security.Oidc`.
