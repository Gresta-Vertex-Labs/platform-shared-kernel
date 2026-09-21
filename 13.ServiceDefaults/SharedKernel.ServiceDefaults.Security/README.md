# SharedKernel.ServiceDefaults.Security

**One `IRequestContext` over `12.Security` for every layer that asks who is calling.**

```csharp
builder.Services.AddOidcAuthentication(builder.Configuration);   // registers IUserContext
builder.Services.AddSharedKernelRequestContext();                // IRequestContext over IUserContext + ITenantProvider
```

The registered `IRequestContext` is what `05.Application`'s `AuthorizationBehavior` checks permissions against,
what the caching behaviors scope keys by, and what `06.Persistence` attributes audit columns, filters
tenant rows and writes audit records with. There is no separate persistence or pipeline bridge.

| `IRequestContext` | From |
| --- | --- |
| `IsAuthenticated` | `IUserContext.IsAuthenticated` |
| `UserId` | `SubjectId`, else `ClientId`; `null` when unauthenticated |
| `TenantId` | `ITenantProvider.TenantId`; `Guid.Empty` becomes `null` (fails closed) |
| `ActorKind` | `IdentityKind`: `User` → `User`, `ServicePrincipal` → `Service`, otherwise `System` |
| `ClientId`, `SessionId` | `IUserContext` |
| `HasPermissionAsync` | `IUserContext.HasPermission` (ordinal) |

`ITenantProvider` defaults to `UserContextTenantProvider` (the token's tenant claim) unless one is already
registered.

The registration uses `Add`, so it replaces the fail-closed anonymous default that
`SharedKernel.Persistence.EfCore` registers, regardless of call order.
