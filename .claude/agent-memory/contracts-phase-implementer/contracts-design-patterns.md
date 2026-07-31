---
name: contracts-design-patterns
description: Key implementation decisions and gotchas from SK.04.Design — STJ contexts, naming collision, EventEnvelope shape, test patterns
metadata:
  type: project
---

## PagedList<T> constructor visibility
The constructor must be `internal` (not `private`) with `[JsonConstructor]` for STJ source-generated deserialization to work. Private constructors cause SYSLIB1222 from the source generator. `Create` is still the only externally intended construction path — the internal constructor is not part of the public API.

**Why:** STJ source generator cannot access private constructors even when marked [JsonConstructor].

## Envelope namespace/type collision — RESOLVED by WO-052/P-328 (target v2.0.0)
Historical: the `Envelope` type used to live in namespace `SharedKernel.Contracts.Envelope`, requiring a using-alias workaround (`using EnvelopeNs = SharedKernel.Contracts.Envelope;`) to avoid ambiguity with the type name itself.
As of WO-052 (design finalized 2026-07-31, SK.04.Design fully ● complete), the fix is: rename namespace to `SharedKernel.Contracts.Envelopes` (plural) and folder `Envelope/` → `Envelopes/`. Type members/factories/implicit operators are byte-identical — namespace-only move. No alias workaround needed or should be used against the new namespace.
As of the same design pass, code has NOT yet been moved (verified 2026-07-31: `Envelope/Envelope.cs` and `Envelope/EnvelopeT.cs` still exist under the old namespace). The rename lands in Scaffold (S-06: `git mv Envelope/ Envelopes/`) and Core (C-08: namespace edit + update `ResultEnvelopeExtensions`' `using`, `ContractsJsonContext` entries, README, test `TestJsonContext`). Target package version on release: `2.0.0` (major/breaking — first breaking change in this package's history).

## EventEnvelope.Wrap — non-generic static class
`Wrap<TEvent>` lives on a non-generic static class `EventEnvelope` (not on `EventEnvelope<TEvent>` itself). This allows generic type inference — callers write `EventEnvelope.Wrap(domainEvent, ...)` rather than `EventEnvelope<OrderPlacedEvent>.Wrap(...)`.

## EventId vs EnvelopeId distinction
Per CLAUDE.md: `EventEnvelope<TEvent>.EventId` is **copied from** `TEvent.Id` — it is NOT a new envelope-level identity. There is no `EnvelopeId` property. This differs from what the system-level spec initially described (it mentioned `EnvelopeId`). The 04.Contracts CLAUDE.md is authoritative.

## STJ test context pattern
Tests cannot use `ContractsJsonContext.Default` directly for concrete generic types (e.g. `PagedList<string>`) because the context only registers `PagedList<object>`. The correct pattern:
1. Create a test-level `partial JsonSerializerContext` (`TestJsonContext`) with `[JsonSerializable(typeof(PagedList<string>))]` etc.
2. Build `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }`.
3. Add `TestJsonContext.Default` then `ContractsJsonContext.Default` to `TypeInfoResolverChain`.

**Why:** `[JsonSourceGenerationOptions]` on a context class does NOT auto-apply naming to `JsonSerializerOptions` when using `TypeInfoResolverChain`. The naming policy must be set on the options object.

## ContractsJsonContext visibility
`ContractsJsonContext` is `internal`. `AssemblyInfo.cs` declares `[assembly: InternalsVisibleTo("SharedKernel.Contracts.Tests")]` to make it accessible in tests.

## CorrelationId is nullable string?
Per CLAUDE.md spec: `CorrelationId` on `EventEnvelope<TEvent>` is `string?` — null is valid for root events. The CLAUDE.md was updated from an earlier spec that described it as non-nullable with a guard.
