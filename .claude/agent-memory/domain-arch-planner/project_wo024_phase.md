---
name: project-wo024-phase
description: WO-024 (P-152) — locked design decisions for StronglyTypedIdJsonConverterFactory in SharedKernel.Domain
metadata:
  type: project
---

WO-024 (P-152) added 6 tasks (D-33, S-06, C-37, T-30, DO-29, P-10) for a generic STJ `JsonConverter`/`JsonConverterFactory`
pair for `StronglyTypedId<TValue>`, replacing the prior "consuming services must provide their own converter" note.

**Why:** Across "hundreds of services," every team was writing a per-ID `JsonConverter<OrderId>` etc. `System.Text.Json`
is part of the `net10.0` shared framework (not an added NuGet dependency), so this does not violate the zero-external-NuGet
rule for `SharedKernel.Domain`.

**How to apply:** Treat all decisions below as locked. Increment D/S/C/T/DO/P IDs from D-33/S-06/C-37/T-30/DO-29/P-10.

Key locked decisions:

- New subfolder `StronglyTypedIds/Serialization/` inside `SharedKernel.Domain/` — namespace `SharedKernel.Domain.StronglyTypedIds.Serialization`.
- Two sealed types: `StronglyTypedIdJsonConverterFactory` (extends `JsonConverterFactory`) and
  `StronglyTypedIdJsonConverter<TStronglyTypedId, TValue>` (extends `JsonConverter<TStronglyTypedId>`).
- Supported `TValue` shapes: `Guid`, `int`, `long`, `string` only. `CanConvert` checks `Type.BaseType` closes
  `StronglyTypedId<TValue>` for one of these four — anything else falls back to default STJ record serialization
  (object-wrapper `{ "value": ... }`).
- Wire format is the **bare primitive** — never `{ "value": ... }`. This is the whole point: an `OrderId` serializes
  identically to a `Guid`.
- Construction strategy: `CreateConverter` uses `Activator.CreateInstance` on the closed generic converter type
  (called once per closed type, cached by `JsonSerializerOptions`). Inside the converter, a `Func<TValue, TStronglyTypedId>`
  activator is compiled once via `Expression.New` over the concrete type's public `(TValue Value)` primary constructor
  and cached for the converter's lifetime — same class of startup-time, type-inspection-only reflection as the existing
  `DomainEventVersionHelper.GetVersion(Type)` precedent. No per-element reflection.
- Concrete strongly-typed ID types MUST follow `public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);`
  — i.e. a public primary constructor `(TValue Value)`. A type that hides/omits this constructor fails at first
  converter use with `InvalidOperationException`, not at `CreateConverter` time.
- Strictly opt-in — `SharedKernel.Domain` never calls `JsonSerializerOptions.Converters.Add` itself.
  Registration: `options.Converters.Add(new StronglyTypedIdJsonConverterFactory())`.
- AOT caveat documented as consumer-facing, not a hard blocker: `Expression.Compile()` under `PublishAot=true` may
  need the interpreter fallback or `RequiresDynamicCode`/`RequiresUnreferencedCode` annotations.
- SharedKernel.Domain version bumped to 1.6.0 after WO-024.

Related: [[project-wo014-wo016-phases]] [[project-wo010-wo011-phases]] [[project-domain-foundation]]
