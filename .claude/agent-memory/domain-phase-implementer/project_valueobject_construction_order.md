---
name: project_valueobject_construction_order
description: Empirically confirmed C# construction-order rule for ValueObject test doubles — field-initializer vs constructor-body property assignment relative to base Validate() call
type: project
---

`ValueObject`'s base constructor calls `Validate()` immediately (documented hazard in `ValueObject.cs`'s own XML remarks). When writing test doubles (or real value objects) whose `Validate()` reads a property, the property's assignment mechanism determines whether `Validate()` sees the real value or a default:

- **Field-initializer / primary-constructor capture** (e.g. `private sealed class Money(decimal amount, string currency) : ValueObject { public string Currency { get; } = currency; ... }`) — the property IS visible/correctly set inside the base `ValueObject()` constructor's `Validate()` call. Confirmed empirically via a real failing-then-passing test during WO-051/C-42 implementation, not assumed from prose.
- **Classic constructor-body assignment** (e.g. `private Money(decimal amount, string currency) { Amount = amount; Currency = currency; }`) — the property is STILL DEFAULT (`null`/`0`) when `Validate()` runs, because constructor-body statements execute after the base constructor entirely completes. A `Validate()` referencing such a property will unconditionally see the default value regardless of what was actually passed to the constructor — this produces a silent, always-failing (or always-passing) validation bug in test doubles, not a compile error.

**Why:** This is the classic C# "virtual/abstract call in constructor sees uninitialized derived state" gotcha (base ctor body → then derived field initializers → then derived ctor body), EXCEPT primary-constructor-captured field initializers are treated as running before the base call completes when there's no explicit `: Base(args)` forwarding — this is the deliberate mechanism `SingleValueObject<TValue>` already exploits (see its own field-initializer comment) and is safe to rely on.

**How to apply:** When writing a `ValueObject` test double whose `Validate()` needs to read a constructor argument, always use the field-initializer/primary-constructor pattern, never assign in the constructor body. This mirrors the two safe patterns `ValueObject.cs`'s own `<remarks>` documents (field-initializer assignment, or a private/protected ctor + `Result`-returning static factory). See [[project_sk03_published_pattern]] and `03.Domain/CLAUDE.md`'s ValueObject construction-order-hazard remarks for the authoritative doc version.
