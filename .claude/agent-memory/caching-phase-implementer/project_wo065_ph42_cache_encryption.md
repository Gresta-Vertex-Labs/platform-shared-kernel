---
name: project-wo065-ph42-cache-encryption
description: WO-065 Phase 42 (CacheEncryptionAtRest) — Redis Hash-vs-String storage discovery, stale-package-pin recurrence, decorator-ordering-guard pattern via DI marker
metadata:
  type: project
---

# WO-065 Phase 42 — Cache-Value Encryption at Rest (P-433)

Shipped `CacheEncryptionSerializer`/`CacheEncryptionOptions`/`AddCacheEncryption` in
`SharedKernel.Caching.FusionCache`, an AES-GCM decorator over `IFusionCacheSerializer`
mirroring `BrotliCacheSerializer`'s shape (magic bytes `0x45 0x4E`/"EN" vs Brotli's
`0x42 0x52`/"BR"). Built on `01.Core/SharedKernel.Cryptography`'s
`ISymmetricEncryptionService`. 234/234 FusionCache + 31/31 Redis tests green.

## Discovery: Microsoft.Extensions.Caching.StackExchangeRedis stores entries as a Redis HASH, not a string

**Check this before writing any future raw-Redis-value assertion in this domain.**

- `AddRedisL2`'s underlying `IDistributedCache` (`Microsoft.Extensions.Caching.StackExchangeRedis`,
  pinned `10.0.0`) stores every L2 entry via its own internal Lua script as a Redis **Hash**
  with fields `absexp`/`sldexp`/`data` — never a plain Redis string.
- A raw `IDatabase.StringGetAsync(key)` against an L2 key throws
  `StackExchange.Redis.RedisServerException: WRONGTYPE Operation against a key holding the
  wrong kind of value`. The correct raw read is
  `IDatabase.HashGetAsync(key, "data")`.
- **Why this was invisible before:** the existing `L2KeyPrefixIntegrationTests.cs` (which
  *looks* like it proves raw-value reads) only ever calls `KeyExistsAsync`/`server.Keys(...)`
  — both type-agnostic. It never actually reads the value bytes. Phase 42's `CE-07` was the
  first test in this domain to attempt a genuine raw-value read, and it surfaced immediately.
- Diagnostic technique used to find this: add a temporary `db.KeyTypeAsync(key)` check that
  throws with the type name + `server.Keys("*")` dump when the type isn't what's expected —
  faster than guessing from StackExchange.Redis exception text alone.

## Decorator ordering-guard pattern: DI marker beats live-instance type check

The phase spec's own Implementation Rule suggested detecting "is the current serializer
already a `CacheEncryptionSerializer`" via "a type check on the DI-resolved serializer
instance" — i.e., build a temporary `ServiceProvider` mid-`AddBrotliCompression()` call and
inspect the resolved instance's type. **Judged fragile and rejected**: other prerequisites
(e.g. a real `IEncryptionKeyProvider`) may not yet be resolvable at that point in the
registration sequence, and building a provider mid-config is expensive/order-sensitive.

**Chosen alternative, used instead:** register a small marker options type
(`CacheEncryptionOptions`, `Enabled = true`) as a singleton when `AddCacheEncryption()` runs;
`AddBrotliCompression()` checks `services.Any(sd => sd.ServiceType == typeof(CacheEncryptionOptions))`
and throws if present. Behaviorally equivalent, zero risk, and matches this domain's own
established Phase 19 guard idiom (`services.Any(sd => sd.ServiceType == typeof(...))`).
Reuse this marker-registration pattern for any future "was X already applied to this
decorator chain" DI-time check in this domain — don't reach for a live mid-registration
`BuildServiceProvider()` unless there's no other way.

## Decorator wrapping: "wrap whatever's currently registered" needs descriptor introspection, not `sp.GetRequiredService`

`AddCacheEncryption()` must wrap *whichever* `IFusionCacheSerializer` is currently
registered (base STJ serializer, or an already-applied `BrotliCacheSerializer`) — unlike
`AddBrotliCompression()`, which always resolves the fixed concrete
`FusionCacheSystemTextJsonSerializer` type regardless of what's registered (a pre-existing
quirk, not something this phase touched). Naively calling
`sp.GetRequiredService<IFusionCacheSerializer>()` inside the new factory would recurse into
itself once `Replace()` swaps the registration. Fix: capture the **old** `ServiceDescriptor`
before calling `Replace`, and build a small helper that reproduces its resolution
(`ImplementationInstance` → return directly; `ImplementationFactory` → call it;
`ImplementationType` → `ActivatorUtilities.CreateInstance`), then use that captured resolver
— never `sp.GetService<TInterface>()` — inside the new decorator's factory. This is the
correct manual "decorate whatever's currently registered" pattern for this codebase (no
Scrutor dependency exists here).

## Recurring pattern: stale package pins below `SharedKernel.Testing`'s transitive floor

Second time this exact defect class has surfaced in `02.Caching` test projects (first was
the `Testcontainers.Redis` 4.4.0-vs-4.13.0 conflict in WO-050/Phase 39 — see
`project_wo050_gold_standard.md`). This time it was `SharedKernel.Caching.Redis.Tests.csproj`'s
own direct `Microsoft.Extensions.DependencyInjection`/`Microsoft.Extensions.Logging`/
`Polly.Core` pins (`10.0.0`/`10.0.0`/`8.5.2`), stale against `SharedKernel.Testing`'s
transitive floor (`10.0.9`/`10.0.9`/`8.7.0`) — the project failed `NU1605` restore
**even before** this phase's own new file existed (confirmed via isolated repro with the
file temporarily removed). Bumped all three pins in that one `.csproj`.

**Left deliberately untouched** (out of this phase's scope): `SharedKernel.Caching.Redis.Core.Tests`,
`.Redis.DistributedLocking.Tests`, `.Redis.PubSub.Tests` all carry the identical stale-pin
pattern (confirmed via a full-solution `dotnet build` after this phase's own fix — they
still fail `NU1605`). **A future session should do a dedicated pass bumping all
`02.Caching.*.Tests.csproj` `Microsoft.Extensions.*`/`Polly.Core` pins to match
`SharedKernel.Testing`'s current floor** — this will keep recurring domain-wide as
`16.Testing` bumps its own floor over time, since nothing currently keeps sibling test
projects' explicit pins in sync with it.

## Note on real crypto in unit tests

For genuine round-trip/tamper-detection tests (not just DI-wiring sanity), use the REAL
`AesGcmEncryptionService` + a small in-memory `IEncryptionKeyProvider` test double — never
`Substitute.For<ISymmetricEncryptionService>()`, which proves nothing about actual
encryption/authentication behavior. `16.Testing/SharedKernel.Testing.Cryptography` already
ships `FakeEncryptionKeyProvider` (public) for exactly this — reference it directly instead
of hand-rolling a duplicate when the test project already has (or can cheaply gain) a
`SharedKernel.Testing` `ProjectReference`. For DI-wiring-only tests (does the right type get
resolved, does the guard throw), `Substitute.For<ISymmetricEncryptionService>()` is fine and
faster.
