---
name: project-wo081-ph46-encrypted-cache-service
description: WO-081 Phase 46 (CacheEncryptionAadBinding) — CacheEncryptionSerializer retired for EncryptedCacheService (ICacheService-level decorator), FusionCache envelope nesting discovery, factory-recapture test pattern for AsyncLocal-free proofs
metadata:
  type: project
---

> WO-086 (2026-09): the core `SharedKernel.Testing` now references Foundation + Model only — it no longer drags in `06.Persistence` or the rest of the repo; per-capability fakes are in `SharedKernel.{Capability}.Testing`, fixtures in `SharedKernel.Testing.Internal`.

# WO-081 Phase 46 — Cache Encryption AAD Key-Binding (P-497)

Retired Phase 42's `CacheEncryptionSerializer` (an `IFusionCacheSerializer` decorator) and
replaced it with `EncryptedCacheService` (an `ICacheService` decorator), in
`SharedKernel.Caching.FusionCache`. Root cause: `IFusionCacheSerializer.Serialize<T>`/
`Deserialize<T>` never receive the cache key — a serializer-level decorator cannot derive
key-bound AAD no matter how it's written. `ICacheService` is the first layer where the key is
an explicit parameter on every member. This is a **permanent** design decision recorded in
`02.Caching/CLAUDE.md`'s "Cache-value encryption rules" — do not suggest reverting to a
serializer-level decorator in a future session.

Key production files: `Encryption/EncryptedCacheService.cs` (new), `Serialization/
BrotliPayloadCodec.cs` (new, extracted from `BrotliCacheSerializer`), `Serialization/
CacheSerializationOptions.cs` (new, DI holder for shared `JsonSerializerOptions`),
`Serialization/EncryptedPayloadJsonContext.cs` (new, STJ source-gen for `EncryptedPayload`),
`Extensions/CacheEncryptionCachingBuilderExtensions.cs` (rewritten). `CacheEncryptionSerializer.cs`
deleted outright (was `internal sealed`, never public API).

## Discovery: FusionCache's own wire envelope nests the stored value under "Value"

The raw JSON `Microsoft.Extensions.Caching.StackExchangeRedis` writes to Redis for an L2 entry
is **FusionCache's own distributed-entry envelope** — top-level properties `Value`/`Timestamp`/
`LogicalExpirationTimestamp`/`Tags`/`Metadata` — not the stored `T` directly. When `T =
EncryptedPayload` (this phase's new wire shape), `EncryptedPayload`'s own fields (`KeyId`/
`Nonce`/`Ciphertext`/`Tag`) live nested inside the envelope's `Value` property, not at the JSON
root. A raw-Redis-read integration test asserting on root-level property names will fail against
the real shape — confirmed by actually running the Testcontainers-backed test (not caught by
static reasoning alone). Fix: assert `JsonDocument.Parse` succeeds (proves well-formed JSON, not
an opaque blob) plus a case-insensitive whole-payload substring search for the expected field
names, rather than asserting on root-level `TryGetProperty`. Also don't assume a naming-policy
casing (PascalCase vs camelCase) for the nested envelope — this domain has never pinned one, and
`FusionCacheSystemTextJsonSerializer`'s parameterless constructor's exact default options are a
third-party library implementation detail not worth asserting on directly.

## Test pattern: proving a wrapped-factory closure needs no AsyncLocal, without real FusionCache timing

To prove `EncryptedCacheService.GetOrSetAsync`'s wrapped `Func<CancellationToken,
ValueTask<EncryptedPayload>>` factory survives FusionCache's real eager-refresh background
re-invocation (`CachePolicy.WithEagerRefresh`) with no ambient/`AsyncLocal` dependency, do NOT
try to trigger real eager-refresh timing in a unit test (flaky, slow, and the memory/state-map
already recorded that this exact background-continuation guarantee is *not independently
verifiable* against the FusionCache library — that's why the AsyncLocal alternative was rejected
in the first place). Instead: build a bespoke `ICacheService` test double whose `GetOrSetAsync<T>`
captures the `Func<CancellationToken, ValueTask<T>>` factory it receives (when `typeof(T) ==
typeof(EncryptedPayload)`) into a public field, then in the test body re-invoke that captured
factory a second time from an unrelated `Task.Run` continuation and prove it still produces a
correctly-encrypted/decryptable payload. This directly tests the actual property that matters
(closure captures only local state, works from any execution context) without depending on
FusionCache's internal scheduling at all — deterministic, fast, and a stronger proof than trying
to observe real eager-refresh firing.

## `AddCacheEncryption()` rewrite: reused `ResolveExistingFactory` for two lazy in-place type-checks, not a mid-registration `BuildServiceProvider()`

Phase 42's own prior session already established the lesson "don't build a temporary
`ServiceProvider` mid-registration to type-check a live instance — fragile, other prerequisites
may not be resolvable yet." Phase 46's `AddCacheEncryption()` genuinely needs to know, at
registration time, whether the currently-registered `IFusionCacheSerializer` is a
`BrotliCacheSerializer` (to unwrap it via `.Inner` and set `compressionEnabled`). Resolution: keep
using the deferred-factory pattern Phase 42 already used for wrapping (`ResolveExistingFactory`
captures how to reproduce whatever a `ServiceDescriptor` would resolve to, given a real
`IServiceProvider` handed to it later) — but call this *same* captured delegate independently from
**two** separate `Replace()` factories (the `IFusionCacheSerializer` unwrap, and the
`ICacheService`-wrapping `EncryptedCacheService` construction). Each factory runs lazily against
the REAL, fully-built container at first resolution — never `services.BuildServiceProvider()`. The
minor cost: the captured original-serializer factory gets invoked twice (once per consuming
factory), producing two harmless, functionally-identical duplicate `BrotliCacheSerializer`/base-STJ
instances — acceptable since both are stateless. This is a legitimate, judged divergence from the
phase spec's literal "type-check the currently-registered IFusionCacheSerializer" wording (which
didn't specify HOW to get an instance to check) — worth defending explicitly in the completion
report/changelog rather than silently doing something different from the brief.

## `EncryptedCacheService`'s compression duty is unconditional, not threshold-aware

The phase spec's own Implementation Rule 1 lists `EncryptedCacheService`'s constructor
parameters exhaustively: inner `ICacheService`, `ISymmetricEncryptionService`, shared
`JsonSerializerOptions`, and a `bool compressionEnabled` — no `CompressionOptions`/threshold/level.
Since `AddBrotliCompression()` itself must receive "zero code change" (an explicit phase
constraint, and its own `.csproj`/file is NOT in the phase's file-level plan), there's no legal
path to carry the per-service-configured `L2ThresholdBytes`/`Level` over to
`EncryptedCacheService` without either modifying `AddBrotliCompression()` or reflecting into a
private field. Chosen resolution: `EncryptedCacheService` calls the extracted
`BrotliPayloadCodec.Compress(bytes, thresholdBytes: 0, CompressionLevel.Fastest)` —
unconditional compression, ignoring whatever threshold/level the caller originally configured via
`AddBrotliCompression(o => ...)`. This is a deliberate, documented simplification (recorded in
CLAUDE.md's "Cache-value encryption rules"), not an oversight — flag it explicitly if a future
phase wants per-service-configurable compression once encryption owns that duty.

## Concurrent multi-domain dispatch: transient red builds from sibling domains are real and self-resolve

This phase ran concurrently with WO-081 sessions in `06.Persistence`, `07.Messaging`,
`15.Integration`, `17.Workflows` (all editing production code for the same upstream `01.Core`
breaking change). `SharedKernel.Caching.Redis.Tests.csproj` transitively references
`16.Testing/SharedKernel.Testing`, which references nearly the whole repo including
`06.Persistence.EfCore` — so a genuinely broken, mid-flight sibling domain build blocked this
domain's own `dotnet test` runs for part of the session with completely unrelated compile errors
(`IConventionEntityType.GetSchema`/etc., nothing to do with caching). Confirmed via `git status`
(uncommitted files in the sibling domains) that this was real concurrent work, not something to
fix. It resolved itself a few minutes later once the sibling agent's own fix landed — no action
was needed on this domain's side. Lesson: when told "expect the solution-wide build to be red,
don't chase it," verify the failing files are genuinely outside your own domain's edited paths
before concluding you're blocked, and periodically retry (don't sleep/poll, just retry between
other work) rather than giving up on running your own tests.
