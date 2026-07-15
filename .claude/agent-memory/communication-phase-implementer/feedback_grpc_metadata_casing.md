---
name: feedback-grpc-metadata-casing
description: Grpc.Core.Metadata normalizes entry keys to lowercase internally — affects test assertions comparing stored keys
metadata:
  type: feedback
---

`Grpc.Core.Metadata.Add`/`Metadata.Entry` normalize entry keys to lowercase internally (verified empirically against `Grpc.Core.Api` 2.80.0). Adding a key such as `"X-Tenant-Id"` (mixed case, e.g. from `01.Core`'s `WellKnownHeaders.TenantId`) is accepted without throwing but is stored/read back as `"x-tenant-id"`.

**Why this matters:** When a test asserts the *literal stored key* of a `Metadata.Entry` after adding an entry via a mixed-case constant (e.g. `cloned[0].Key.Should().Be(WellKnownHeaders.TenantId)`), the assertion fails — the actual stored key is lowercase. The fix is to compare against `WellKnownHeaders.TenantId.ToLowerInvariant()` with an inline comment explaining why, not to revert to a hardcoded lowercase literal (which would reintroduce the exact duplication problem P-260 eliminated).

**How to apply:** Any test that literally reads back a `Metadata.Entry.Key` (not just checks case-insensitive presence via `HasMetadataEntry`) must account for this normalization. Lookups/`HasMetadataEntry`-style checks are unaffected since they're already case-insensitive by design (`GrpcMetadataHelper.HasMetadataEntry` uses `OrdinalIgnoreCase`). This is documented as a formal gRPC rule in `11.Communication/CLAUDE.md` under "gRPC rules" (P-260) — it's also why `TenantIdInterceptor.TenantIdKey`/`CorrelationTracingInterceptor.CorrelationIdKey` can safely source directly from the uppercase-hyphenated `WellKnownHeaders` constants with zero wire-format change.

See also [[project_communication]], [[feedback_grpc_namespaces]].
