# SharedKernel.Persistence.EfCore.Auditing

An append-only, hash-chained audit trail for EF Core 10: `IAuditTrailWriter`/`IAuditQueryService`, a tamper-evident `AuditRecord` chain (each record's hash covers the previous record's hash), a `SaveChanges`-time immutability guard that structurally rejects any `UPDATE`/`DELETE` of an `AuditRecord`, and signed chain-head checkpoints for cheap, truncation-detecting re-verification. An opt-in sibling of `SharedKernel.Persistence.EfCore` — never referenced unless a service calls `WithAuditTrail()`. Distinct from `SharedKernel.Persistence.EfCore`'s own `AuditInterceptor`, which only stamps mutable `CreatedBy`/`ModifiedOn` columns and keeps no history.

## Included types

- `EfCorePersistenceBuilderAuditingExtensions.WithAuditTrail()` — opts in; registers the model configuration, the immutability interceptor, the mutation-guard options extension, and `IAuditTrailWriter`/`IAuditQueryService`
- `EfCorePersistenceBuilderAuditingExtensions.WithAuditChainCheckpoints()` — opts in to signed chain-head checkpoints on top of `WithAuditTrail()`
- `EfAuditTrailWriter` — appends a record to a `(tenant, resource type)` chain, computing its hash from the previous record's hash plus its own content
- `EfAuditQueryService` — resource-history / actor-actions keyset queries, cross-tenant history (behind `ICrossTenantScope`), full-chain and from-checkpoint verification
- `EfAuditCheckpointService` — creates a signed `AuditChainCheckpoint` for a chain's current head
- `AuditRecordImmutabilityInterceptor` — `SavingChanges`-time guard: throws `AuditRecordImmutableException` on any tracked `Modified`/`Deleted` `AuditRecord`
- `AuditRecordMutationGuardInterceptor` — the same guard for `ExecuteUpdateAsync`/`ExecuteDeleteAsync`/raw SQL against the audit table, since those bypass the change tracker entirely
- `AuditChainOptions` — the HMAC signing key (`HmacKeyBase64`, ≥32 bytes) and checkpoint signing key id, validated at startup
- `IAuditChainKeyProvider` / `ConfiguredAuditChainKeyProvider` — resolves the HMAC key; register your own to source it from a secrets manager instead of configuration
- `AuditChainKey` — the `(tenant, resource type)` chain identity value type
- `AuditingMeter` — the `"SharedKernel.Persistence.EfCore.Auditing"` `Meter`, counting record writes and chain-verification runs

## Install

```xml
<ProjectReference Include="..\SharedKernel.Persistence.EfCore.Auditing\SharedKernel.Persistence.EfCore.Auditing.csproj" />
```

Requires `01.Core/SharedKernel.Cryptography`'s `AddSharedKernelCryptography()` (for `IHmacSigner`) to have been called first. Requires `SharedKernel.Persistence.Npgsql`'s `AddSharedKernelNpgsql()` — `EfAuditTrailWriter` takes a mandatory `IDbConnectionFactory` dependency that only it registers, so omitting it fails DI resolution outright. The one genuinely optional part of that registration is `IAdvisoryTransactionLock` for real per-chain append serialization — without a registered one, concurrent appends to the same chain still succeed correctly, just with more unique-constraint retries under contention. Also requires `EfCorePersistenceBuilder<TContext>.WithTransactionalUnitOfWork()` — `EfAuditTrailWriter` also takes a mandatory `IAmbientDbTransaction` dependency that only that call registers. In production, also apply the PostgreSQL migration helper's `CreateImmutabilityTrigger` to the audit table — see `AuditRecordMutationGuardInterceptor`'s remarks for why the application-level guards alone are not sufficient defense against a direct SQL client.

## Quick start

```csharp
services.AddSharedKernelCryptography(configuration);            // IHmacSigner
services.AddSharedKernelNpgsql(configuration);                  // IDbConnectionFactory, IAdvisoryTransactionLock
services.AddSharedKernelEfCore<AppDbContext>(options => ...)
    .WithTransactionalUnitOfWork()                               // IAmbientDbTransaction
    .WithAuditTrail(configuration)                               // section "SharedKernel:Persistence:Auditing"
    .Build();
```

```csharp
public sealed class WithdrawFundsHandler(IAuditTrailWriter auditTrail) : ICommandHandler<WithdrawFunds, Result>
{
    public async Task<Result<Result>> Handle(WithdrawFunds request, CancellationToken ct)
    {
        // ... apply the withdrawal to the aggregate ...

        await auditTrail.RecordAsync(
            new AuditEntry(
                resourceType: "Account",
                resourceId: request.AccountId.ToString(),
                action: "Withdraw",
                beforeSnapshot: beforeJson,
                afterSnapshot: afterJson),
            ct);

        return Result.Success();
    }
}
```

## Verifying a chain

```csharp
// The tenant is resolved from the caller's own ICurrentTenantContext — never a parameter here.
var result = await auditQueryService.VerifyFullChainAsync(resourceType: "Account", ct);
if (!result.IsIntact)
{
    // result.BrokenAtSequence / result.BrokenAtRecordId identify exactly where tampering was detected.
}
```

`VerifyFullChainAsync` cannot detect tail truncation on its own — a chain missing its last N records looks identical to one that genuinely only ever had that many. Detecting that requires a previously-created `AuditChainCheckpoint` and `VerifyChainFromCheckpointAsync`.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts and implementation rules.
