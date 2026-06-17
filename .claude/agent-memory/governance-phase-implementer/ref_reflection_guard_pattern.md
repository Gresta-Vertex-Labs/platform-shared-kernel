---
name: ref-reflection-guard-pattern
description: SK0012 ReflectionGuard implementation — IL walk for MakeGenericMethod, exemption registry, InternalsVisibleTo pattern, stale Phase Backlog closure
metadata:
  type: reference
---

## SK0012 ReflectionGuard (WO-024 P-153)

**Rule**: `ReflectionGuardRules.NoMakeGenericMethodReflection(Assembly)` enforces the
platform-wide prohibition on `GetMethod/MakeGenericMethod/Invoke` reflection-based generic dispatch.

**Why IL not Roslyn**: `MethodInfo.MakeGenericMethod` is called on a runtime variable — no
compile-time syntax pattern detects it reliably. IL inspection via Mono.Cecil is the only
reliable mechanism.

**Key implementation decisions**:
- `NoMakeGenericMethodReflectionPredicate` checks `MethodReference.Name == "MakeGenericMethod"` (exact, case-sensitive) — unique in BCL, no namespace check needed
- `ReflectionExemptionRegistry` uses `HashSet<(string TypeFullName, string MethodName)>` with default value-tuple equality (not `StringComparer.Ordinal`) — `new()` constructor works
- `Register`/`Unregister` are `internal` so tests can drive the exemption path without polluting public API
- `InternalsVisibleTo` added via `AssemblyAttribute` MSBuild element in `SharedKernel.ArchitectureTests.csproj` — NOT via `AssemblyInfo.cs`
- `.AreNotAbstract()` filter in factory method excludes compiler-generated async state machine types

**Test patterns (T-113, T-114, T-115)**:
- Fire path: `CompileInMemory("Fixture.Reflection.Violation", source)` → assembly with `method.MakeGenericMethod(typeof(int))` IL
- Pass path: typed dispatch code with no `MakeGenericMethod` IL opcode
- Exemption path: `Register` → rule passes; `Unregister` in `finally` → rule fails again

**Motivating incident**: P-147 (WO-024) — `EncryptionRotationService.LoadBatchAsync` in 06.Persistence shipped `GetMethod("LoadBatchAsync").MakeGenericMethod(entityType).Invoke(...)` while CLAUDE.md documented expression trees as the gold standard.

**SK0012 diagnostic ID**: next sequential after SK0011 in the general-purpose SK0001–SK00N block. NOT in the 02xx, 03xx, or 07xx domain-specific blocks.

## Stale Phase Backlog Closure Pattern

When the `state-map-phase` skill is invoked for a `phase_key` and root propagation fires, also close all related stale Phase Backlog entries manually (no automation):

**Pattern**: Find Phase Backlog entries with `Status: ◐ Dispatched` for the same domain. Change each to `Status: ● Complete`. Append a single consolidated changelog entry: `P-NNN, P-MMM → ● Complete — stale {Domain} Phase Backlog entries closed (state-map-phase)`.

**This session closed**: P-009, P-034, P-056, P-063, P-075, P-083, P-096, P-103, P-110, P-114, P-123, P-153 for 00.Governance — all were `◐` Dispatched but their implementation work was complete in prior sessions.

**Root state-map Domain Summary Board**: updated to reflect the LATEST completed phase key name and summary done, not the very first phase.
