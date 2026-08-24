---
name: wo065-p438-tenant-cache-status
description: WO-065/P-438 FakeTenantCacheService + cache-encryption interop status — closed 2026-08-24
metadata:
  type: project
---

WO-065/P-438 (`Caching/FakeTenantCacheService`, `AddFakeTenantCacheService()`, plus a fake-encryption-seam
interop proof) is CLOSED end to end as of 2026-08-24. All six `16.Testing` phase keys (Design/Scaffold/
Core/Tests/Docs/Published) are `●` again. Root Phase Backlog P-431 and P-438 both closed to `●` Complete
in the same session (via state-map-phase Step S8c — every phase key in the domain was `●`/`—`, and
neither entry's acceptance criteria named another domain).

**Why:** The phase had been blocked earlier the same day (2026-08-24) because `02.Caching`'s
`ITenantCacheService` (Phase 44/P-435) and `AddCacheEncryption()` (Phase 42/P-433) hadn't shipped yet.
By the time this Core-phase implementation session ran (same calendar day), `02.Caching` had shipped
both — confirmed by re-reading the real `.cs` files, not trusting the phase brief's "blocked" framing.

**How to apply:** If a future session sees `16.Testing/state-map.md`'s `## Blocked` section still
naming C-126/C-127/T-87/T-88 or `02.Caching`'s `ITenantCacheService`/`AddCacheEncryption()` as
unshipped, that is now stale — re-verify against `02.Caching/SharedKernel.Caching.Abstractions/ITenantCacheService.cs`
and `02.Caching/SharedKernel.Caching.FusionCache/Extensions/CacheEncryptionCachingBuilderExtensions.cs`
directly; both exist as of 2026-08-24.

**Implementation notes:**
- `ITenantCacheService` shipped with ZERO drift from the Design-phase (D-209) draft: 5 members
  (`GetAsync<T>`/`SetAsync<T>`/`GetOrSetAsync<T>`/`RemoveAsync`/`RemoveByTagAsync`), each with a
  mandatory non-defaulted `tenantId` first parameter, `(entity, id)` never a pre-built key.
- `FakeTenantCacheService` backing store: `ConcurrentDictionary<(TenantId, Entity, Id), object?>` plus
  a `(TenantId, Tag)`-keyed tag index for tenant-scoped `RemoveByTagAsync`. A `_keyTags` reverse-map
  (per-key current tag set) was needed beyond the design prose — without it, a re-`SetAsync` on the
  same key with a different/empty tag set would leave stale tag-index entries pointing at a key that
  no longer carries that tag.
- `AddCacheEncryption(this ICachingBuilder)` requires only a registered `ISymmetricEncryptionService`
  (any implementation) — `Cryptography/FakeSymmetricEncryptionService` (already shipped, P-300/WO-049)
  satisfies it with zero new code. But composing a full `AddSharedKernelCaching().AddCacheEncryption()`
  pipeline and resolving `ICacheService` in a test also requires `services.AddLogging()` — needed
  transitively by `FusionCacheService`'s constructor (`ILogger<FusionCacheService>`). This was not
  called out anywhere in the original design text; discovered only by running the test and seeing
  `InvalidOperationException: Unable to resolve service for type ILogger<FusionCacheService>`.
- The T-88 interop test required a NEW scoped `ProjectReference` from
  `SharedKernel.Testing.SelfTests.csproj` to `SharedKernel.Caching.FusionCache` (production package) —
  this project only, `SharedKernel.Testing` itself stays untouched. Mirrors the established
  "SelfTests may reference a real production package under test" precedent (see
  `Security/ApiKeyRotationScenarioBuilderTests.cs`'s T-84), though T-84 itself never actually added
  the reference (its interop half stayed deferred) — this is the first time that precedent was
  actually exercised with a real added `ProjectReference`.
- `FakeRenewableLock.FencingToken` (in `Caching/FakeDistributedLockService.cs`) was checked per this
  session's brief: it's a settable `long` defaulting to 1, no auto-increment on `RenewAsync` — added
  out-of-jurisdiction alongside `02.Caching` Phase 43/P-434 as a compile-only shim. Confirmed still
  adequate for compile-time conformance but not behaviorally faithful. No task anywhere in this
  domain's state-map or CLAUDE.md currently tracks fixing it — only the in-code comment references
  `02.Caching/CLAUDE.md`'s Phase 43/FT-10. Left as-is (out of scope for C-126/C-127); a future session
  should consider adding a real tracked task for it rather than relying on the comment alone.

Full regression: `dotnet test SharedKernel.Testing.SelfTests.csproj --configuration Release` →
1034/1034 passing, zero regressions.
