---
name: contracts-design-patterns
description: Key implementation decisions and gotchas from SK.04.Design — STJ contexts, naming collision, EventEnvelope shape, test patterns
metadata:
  type: project
---

## PagedList<T> constructor visibility
The constructor must be `internal` (not `private`) with `[JsonConstructor]` for STJ source-generated deserialization to work. Private constructors cause SYSLIB1222 from the source generator. `Create` is still the only externally intended construction path — the internal constructor is not part of the public API.

**Why:** STJ source generator cannot access private constructors even when marked [JsonConstructor].

## Envelope namespace/type collision
The `Envelope` type lives in namespace `SharedKernel.Contracts.Envelope`. Consuming code (including tests) must use a using alias or fully-qualified names to avoid ambiguity:
```csharp
using EnvelopeNs = SharedKernel.Contracts.Envelope;
// then: EnvelopeNs.Envelope.Ok()
```

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
