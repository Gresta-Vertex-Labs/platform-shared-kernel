---
name: ref_string_constants_detector_pattern
description: StringConstantsClassDetector + HealthCheckConstantsUsageRules (WO-028 P-178) — third Mono.Cecil IL technique (field-shape + literal-value resolution), const-folding pitfall for Ldstr vs Ldsfld test fixtures
metadata:
  type: reference
---

## StringConstantsClassDetector (WO-028 P-178)

Third distinct Mono.Cecil technique in 00.Governance, after (1) opcode-presence
(`NoMakeGenericMethodReflectionPredicate`) and (2) Ldstr literal-collection
(`NoConflictingLivenessReadinessTagsPredicate`). This one is **field-shape + literal-value
resolution**: walks `TypeDefinition.Fields`, not method bodies, to build a reusable
(declaringType, fieldName, value) tuple set across an assembly. Lives in `Predicates/` despite
not being an `ICustomRule` — co-located with its sole consumer
(`NoBareHealthCheckLiteralWhereConstantsExistPredicate`) for discoverability, per the phase spec.

**Shape contract**: `TypeDefinition.IsAbstract && TypeDefinition.IsSealed` (no `IsStatic` flag
exists on `TypeDefinition` in IL — `static class` always compiles to `abstract sealed`), at
least one field, every field either `IsLiteral` (`const string`) or `IsInitOnly && IsStatic`
(`static readonly string`), both restricted to `FieldType.FullName == "System.String"`.
Mixed-type classes (string consts alongside non-string consts) still qualify — non-string fields
are skipped, not disqualifying.

## Critical pitfall: const-folding erases Ldsfld at the call site

**`const string` field references are erased by the C# compiler at every consuming call site** —
Roslyn const-folds them into a bare `Ldstr` literal with NO `Ldsfld`, no trace the value came
from a constant. Only `static readonly string` field references compile to `Ldsfld` at the
consuming site. Confirmed via direct Mono.Cecil IL dump (see below) — this is not documented
anywhere in the phase spec and had to be discovered empirically when T-140 (the "pass path: field
access instead of literal" test) kept failing with a `const string` fixture.

**Practical implication**: any rule/fixture relying on "field access (Ldsfld) vs literal (Ldstr)"
to distinguish compliant from non-compliant usage MUST use `static readonly string` in the
constants-class fixture, never `const string`, for the pass-path test case. The fire-path test
(bare literal duplicating a constant) works fine with either field kind, since it only cares
about the literal at the violating call site, not the constants class's own field kind.

**Verification technique used**: wrote a throwaway console app referencing `Mono.Cecil` (cannot
load `Microsoft.AspNetCore.App.Ref` reference-assembly DLLs via raw `Assembly.LoadFrom` —
`BadImageFormatException: Cannot load a reference assembly for execution` — but Mono.Cecil's
`AssemblyDefinition.ReadAssembly` reads metadata-only DLLs fine). Compiled fixture source via
`CSharpCompilation`, emitted to a `MemoryStream`, fed the bytes to
`AssemblyDefinition.ReadAssembly(stream)`, dumped every `MethodBody.Instructions` to confirm
exact opcodes. Recommended technique for any future "what IL does Roslyn actually emit for X"
question in this domain — faster and more reliable than guessing from C# semantics.

## Confirming real BCL declaring-type names before writing a predicate

For `HealthCheckConstantsUsageRules`, the phase spec said "confirm the exact declaring-type name
during implementation, don't guess." Located the real .NET 10 reference assemblies under
`C:\Program Files\dotnet\packs\Microsoft.AspNetCore.App.Ref\10.0.7\ref\net10.0\` and read them
with Mono.Cecil (same load-via-stream technique, or `AssemblyDefinition.ReadAssembly(path)`
directly works for on-disk reference DLLs — only execution-loading via `Assembly.LoadFrom`
fails for reference assemblies, not Cecil's metadata-only read). Confirmed:
- `Add(HealthCheckRegistration)` → `Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder`
  (interface) and `.HealthChecksBuilder` (concrete), both in
  `Microsoft.Extensions.Diagnostics.HealthChecks.dll`.
- `AddCheck` overloads → two extension-method host classes,
  `Microsoft.Extensions.DependencyInjection.HealthChecksBuilderAddCheckExtensions` and
  `.HealthChecksBuilderDelegateExtensions` — handled both with a single
  `declaringTypeName.StartsWith("HealthChecksBuilder")` check rather than an exact-name list,
  which also future-proofs against any third extension-method host class following the same
  convention.
- `HealthCheckRegistration` ctor → `Microsoft.Extensions.Diagnostics.HealthChecks
  .HealthCheckRegistration::.ctor`, confirmed in `.Abstractions.dll`.

## Generality-requirement code review pattern

When a phase spec has an acceptance-critical "must never contain literal string X anywhere in
the implementation" requirement, grep the production files for the forbidden string BEFORE
calling the phase done — XML doc comments count as "the implementation" for this kind of
requirement even though they're prose, not executable code. Found 3 such occurrences in this
phase (doc comments referencing `"HealthCheckTags"`/`"HealthCheckNames"` as illustrative
examples) and reworded them to avoid the literal strings, even though they were harmless
prose-only mentions — safer to satisfy the letter of the acceptance criterion than to argue
about whether doc comments "count." CLAUDE.md prose itself is exempt — root/domain brain files
routinely use real concrete names in offending/compliant pattern examples for every other rule
in this domain; only the predicate/rule source files themselves are in scope for this kind of
check.
