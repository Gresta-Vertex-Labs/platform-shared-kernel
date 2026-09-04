# SharedKernel.DataPrivacy

PII/data-classification taxonomy and masking helpers for the Platform.SharedKernel ecosystem. Zero external dependencies, AOT-compatible. Depends on `SharedKernel.Primitives` only.

## Included

- **`DataClassificationAttribute` / `DataClassification`** — a broad sensitivity tier (`Public` / `Internal` / `Confidential` / `Restricted`) applied to a property or field.
- **`SensitiveDataCategoryAttribute` / `SensitiveDataCategory`** — an orthogonal "what kind of sensitive data" tag (`Pii` / `PaymentCard` / `Credential` / `Health`), applicable alongside `DataClassificationAttribute` on the same member.
- **`PiiMasking`** — pure, allocation-minimal, deterministic, null-safe masking helpers: `Email`, `Phone`, `Pan`, `Suppress`. None of them ever throw.
- **`IDataSubjectRequestHandler`** — the GDPR/KVKK export/erasure contract, plus `DataSubjectExportBundle` and `DataSubjectErasureReceipt`.

## Classification attributes are metadata only — by hard design constraint

**NEITHER ATTRIBUTE IS EVER READ VIA REFLECTION IN PRODUCTION CODE.** Their sole sanctioned consumers are a compile-time Roslyn analyzer (`00.Governance`, tracked separately) and human documentation/code review. The platform already bans reflection-based property walks for structured logging — a classification mechanism that itself required runtime reflection to be useful would directly contradict the rule it exists to support. This means:

- Applying these attributes costs nothing at runtime — no assembly scan, no `Attribute.GetCustomAttribute` call anywhere in this package's own source.
- Usable on any type in any layer, including `03.Domain` and `04.Contracts` — both stay logging-free and dependency-minimal by platform rule, and a pure, unread-at-runtime metadata attribute violates neither constraint.

```csharp
using SharedKernel.DataPrivacy.Classification;

public sealed class CustomerProfile
{
    [DataClassification(DataClassification.Public)]
    public string DisplayName { get; init; } = string.Empty;

    [DataClassification(DataClassification.Restricted)]
    [SensitiveDataCategory(SensitiveDataCategory.Pii)]
    public string NationalId { get; init; } = string.Empty;

    [DataClassification(DataClassification.Restricted)]
    [SensitiveDataCategory(SensitiveDataCategory.PaymentCard)]
    public string CardNumber { get; init; } = string.Empty;
}
```

## PiiMasking — masking helpers

Call these explicitly at the one call site that actually needs a partial reveal — a support-ticket UI showing "card ending in 1111", an audit-log line, a customer-facing confirmation email. `PiiMasking` never decides *when* to mask; it only defines *how*.

```csharp
using SharedKernel.DataPrivacy.Masking;

PiiMasking.Email("j.doe@example.com");        // "j***@example.com"
PiiMasking.Email("a@x.com");                  // "***@x.com" — a 1-char local part is never revealed
PiiMasking.Phone("+1 (555) 123-4567");        // "+* (***) ***-4567" — separators preserved, only digits masked
PiiMasking.Pan("4111-1111-1111-1111");        // "****-****-****-1111" — always exactly the last 4 digits
PiiMasking.Suppress("12345678901");           // "[REDACTED]" — the fixed sentinel, regardless of input
```

Every member is null/empty-safe — `null`, `""`, and whitespace-only input all return `string.Empty` (never throw), except `Suppress`, which returns its fixed `RedactedSentinel` for **every** input including `null`, since its entire purpose is an output that never varies with — and therefore never leaks anything about — the real value.

`Phone`'s reveal window is dynamic (4 digits kept when at least 4 are present, 2 when only 2–3 are present, none for a single digit) so a short number is never over-revealed proportionally. `Pan`'s reveal window is a fixed "last 4" regardless of total length — deliberately different from `Phone`, documented on the method itself.

### Composing with `06.Persistence`'s audit trail

`06.Persistence`'s append-only audit trail (`IAuditTrailWriter`, P-456/WO-071) persists opaque, caller-serialized before/after snapshots — it has no knowledge of which fields on the object it is given are sensitive. A caller writing a classified field into that snapshot should mask it first:

```csharp
var auditSnapshot = new
{
    CustomerId = customer.Id,
    Email = PiiMasking.Email(customer.Email),
    CardNumber = PiiMasking.Pan(customer.CardNumber),
};

await auditTrailWriter.WriteAsync(entry with { After = auditSnapshot }, ct);
```

This is documentation guidance only — `06.Persistence` takes no dependency on this package, and this package takes none on `06.Persistence`.

## IDataSubjectRequestHandler — export/erasure requests

Implement this against **your own service's data only**. This package ships no default implementation — there is no honest generic way to export or erase "everything about a subject" without knowing what a given service actually stores — and no cross-service erasure orchestrator. Coordinating a single data-subject request across every service that might hold data about that subject is explicitly out of scope here; it is a plausible future composition (a `19.Scheduling` job or a `17.Workflows` durable workflow) built on top of this contract once real per-service handlers exist.

```csharp
using SharedKernel.DataPrivacy.DataSubjectRequests;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

public sealed class CustomerDataSubjectRequestHandler(ICustomerRepository customers, IClock clock)
    : IDataSubjectRequestHandler
{
    public async Task<Result<DataSubjectExportBundle>> ExportDataAsync(string subjectId, CancellationToken ct = default)
    {
        Customer? customer = await customers.FindByIdAsync(subjectId, ct);
        if (customer is null)
        {
            return Error.NotFound("customer.not_found", $"No customer found for subject '{subjectId}'.");
        }

        var data = new Dictionary<string, object?>
        {
            ["email"] = customer.Email,
            ["displayName"] = customer.DisplayName,
            ["createdAtUtc"] = customer.CreatedAtUtc,
        };

        return new DataSubjectExportBundle(subjectId, clock.UtcNow, data);
    }

    public async Task<Result<DataSubjectErasureReceipt>> RequestErasureAsync(string subjectId, CancellationToken ct = default)
    {
        int affected = await customers.AnonymizeBySubjectIdAsync(subjectId, ct);

        return new DataSubjectErasureReceipt(subjectId, clock.UtcNow, affected);
    }
}
```

## Rules

- `PiiMasking` never logs, never performs I/O, and never uses reflection anywhere in this package.
- `DataClassificationAttribute`/`SensitiveDataCategoryAttribute` are never read reflectively in production code — see above.
- No DI registration — every type here is either a plain static helper, a plain attribute, or an interface the consuming service implements and wires up itself.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
