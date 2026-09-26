---
name: project_wo065_caching_security_review
description: WO-065 02.Caching fintech-security review — no at-rest encryption, dead options validation, no TLS/mTLS surface, no lock fencing tokens, no structural tenant-isolation enforcement
type: project
---

> WO-086 (2026-09): root CLAUDE.md "Layering Rules" is now "Tiers & Dependency Rules".

WO-065 (2026-08-24): direct user request to re-analyze `02.Caching` against big-fintech gold-standard, this time specifically through a security lens ("analyse the needs... in security part"). This is the domain's **second** gold-standard pass — [[project_wo050_caching_goldstandard_review]] (WO-050, 2026-07-29) covered functional/operational completeness (consumer-verify, cross-pod tag invalidation, batch parallelization, tracing); this pass deliberately did not re-litigate anything WO-050 already shipped. Dispatched six phases (P-433–P-438, all `○` Pending) to root `state-map.md`. No `state-map-phase` calls — 02.Caching/00.Governance/16.Testing were all already `●` Published.

**Why this matters beyond this WO:** confirms the pattern from [[project_wo057_security_goldstandard_review]]/[[project_wo061_servicedefaults_goldstandard_review]] generalizes to infrastructure domains, not just identity/host-composition ones — a domain can pass a full functional gold-standard review (WO-050) and still carry real, unaddressed security gaps that only surface when the review question changes from "is this correct and complete" to "is this fintech-grade secure by default."

**Concrete findings (verified by direct source read of `RedisConnectionOptions.cs`, `RedisConnectionCoreExtensions.cs`, and the full `02.Caching/CLAUDE.md`, not domain-brain prose):**
- `RedisConnectionOptions`'s `[Required]`/`[Range]` `DataAnnotations` are decorative — `AddRedisConnection` builds the options object and reads `.ConnectionString`/`.ConnectTimeoutMs` directly, never through an `IOptions<T>`+`ValidateDataAnnotations()`/`ValidateOnStart()` binding path. A unit test (`RedisConnectionOptionsTests.cs`) validates the attributes only against a manually-constructed object — proves the attributes are *decorated correctly*, not that anything in production ever invokes them. Same "validation attribute exists but is never wired to fail-fast" defect class WO-054/WO-061/WO-064 each found once in their own domains — first confirmed instance in 02.Caching.
- TLS is reachable only by hand-splicing `ssl=true` into the raw connection string (StackExchange.Redis's own `ConfigurationOptions.Parse` supports it transitively); no first-class `Ssl` property exists on `RedisConnectionOptions`, and client-certificate mTLS is not reachable through the connection string at all.
- Zero at-rest cache-value encryption option exists anywhere in the domain, despite `07.Messaging` (P-346/WO-054) and `15.Integration` (P-427/WO-064) both already shipping opt-in AES-GCM payload encryption on the identical `01.Core/SharedKernel.Cryptography` primitive, for the identical "TLS covers transport, this is defense-in-depth for a payload that leaves the process" rationale. `02.Caching` writes every value to Redis — often a shared, multi-tenant cluster — and was the one remaining payload-carrying infrastructure domain with no such option.
- `ITenantCacheKeyProvider.BuildTenantKey` (Phase 29, WO-007) is a bare key-formatting helper with zero structural enforcement — nothing stops a caller in a multi-tenant service from using the non-tenant `ICacheKeyProvider`/raw `ICacheService` and serving cross-tenant data. This predates the mandatory-non-defaulted-tenant-scope convention `09.Search`/`10.Intelligence`/`17.Workflows` each independently converged on later and was never retrofitted.
- `RedLockRenewableLock`'s own already-documented re-acquisition strategy (Phase 23, still-current rule) has an explicitly acknowledged "brief unprotected window" (RedLock.net 2.3.2 has no public `ExtendAsync`, so renewal is dispose-then-recreate) — textbook Kleppmann Redlock-critique territory, with no fencing-token mitigation anywhere in the domain.

**Phases dispatched:**
- P-433 (02.Caching): opt-in AES-GCM cache-value encryption, decorator shape mirrors the shipped `BrotliCacheSerializer` exactly (magic-byte prefix, `ArrayPool`, pass-through legacy values); compress-then-encrypt order enforced by composition, not caller discipline.
- P-434 (02.Caching): fencing tokens on `IDistributedLockService`/`IRenewableLock`, sourced via atomic per-resource Redis `INCR`; additive property on the lock handle, no signature break.
- P-435 (02.Caching): `ITenantCacheService` — `ICacheService` wrapper with mandatory non-defaulted `tenantId`, composing `ITenantCacheKeyProvider` internally so cross-tenant leakage becomes structurally impossible for adopters, not merely avoidable-by-convention.
- P-436 (02.Caching): real Options validation for `RedisConnectionOptions` + explicit `Ssl`/certificate configuration surface + a one-time non-loopback-without-TLS startup `Warning` (never a hard failure — mesh-terminated TLS is legitimate).
- P-437 (00.Governance): `SecureDefaultsAssertion`-style mechanical lock on (a) P-433's compress-then-encrypt ordering and (b) P-436's validation actually executing — depends on P-433/P-436.
- P-438 (16.Testing): `FakeTenantCacheService` + a deterministic passthrough fake for the encryption seam — depends on P-433/P-435.

**Declined (not written as phases):**
- A Redis ACL (per-user permission) configuration wrapper — `StackExchange.Redis`'s connection string already fully expresses `user=`/`password=` pass-through; a dedicated abstraction would add ceremony with no new capability.
- Re-opening WO-050's FusionCache-vs-`HybridCache` or RedLock.net-replacement decisions — both remain correct for the reasons already recorded there ([[project_wo050_caching_goldstandard_review]]); this pass's security lens found nothing that changes either verdict.

`sync-brain` called (root mode): Folder Map row 02 annotated with the WO-065 queue note; three new "What Goes Where" rows added (cache-value encryption, lock fencing tokens, `ITenantCacheService`); one changelog line. No Layering Rules/Package Naming/Abstractions Packages table changes — `ITenantCacheService` is a new interface inside the already-existing `SharedKernel.Caching.Abstractions` package, not a new package.

See also [[project_wo050_caching_goldstandard_review]] for this domain's prior pass, and [[project_wo054_messaging_goldstandard_review]] (07.Messaging, P-346) for one of the two prior domains whose shipped opt-in payload-encryption pattern P-433 directly mirrors — the other is 15.Integration's P-427/WO-064, which has no dedicated memory file (WO-064's detail lives only in root `state-map.md`/`CLAUDE.md`).
