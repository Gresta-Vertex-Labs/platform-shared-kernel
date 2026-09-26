---
name: ref_application_pipeline_rules_pattern
description: ApplicationPipelineRules (WO-036 P-225) — fourth Mono.Cecil technique (generic-constraint structural match), CompilerGenerated exclusion pitfall for async fixtures, PipelineOrderAssertion reflection helper, MediatR test-fixture reference gotchas
metadata:
  type: reference
---

> WO-086 (2026-09): `SharedKernel.Application.Behaviors` is now `SharedKernel.Application.Pipeline` (Host tier) and implements the kernel-owned `IPipelineBehavior<,>` from `SharedKernel.Application` — MediatR is referenced only by `SharedKernel.Application.Mediator.MediatR`. `NoTaskDelayOutsideResilienceBehaviorPredicate` was deleted with `ResilienceBehavior` (P-544). The stream-constraint predicate now matches `SharedKernel.Application.Streaming.IStreamQuery\`1`. The MediatR fixture notes below are historical.

## ApplicationPipelineRules (WO-036 P-225)

Three `ICustomRule` predicates in `Rules/ApplicationPipelineRules.cs` / `Predicates/`:
1. `NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate` — caller-supplied behavior-name
   `HashSet<string>` + forbidden-namespace-prefix `HashSet<string>`, both ctor params (never
   hardcoded), mirrors `HealthCheckTagIntegrityRules`'s caller-supplied-prefix convention.
   Excludes any namespace ending in `.Abstractions` even if it shares a forbidden root prefix.
2. `NoGenericConstraintMatchesStreamRequestPredicate` — fourth distinct Mono.Cecil technique in
   this domain (after opcode-presence, Ldstr-literal-collection, field-shape+value): walks
   `TypeDefinition.GenericParameters[0].Constraints` on `IPipelineBehavior<,>` implementors,
   checking each constraint's structural interface closure against `SharedKernel.Application.Streaming.IStreamQuery\`1` (was
   `MediatR.IStreamRequest\`1` before WO-086).
3. (deleted, P-544) `NoTaskDelayOutsideResilienceBehaviorPredicate` — Call/Callvirt fingerprint on
   `MethodReference.Name == "Delay" && DeclaringType.FullName == "System.Threading.Tasks.Task"`,
   with a `type.Name == "ResilienceBehavior"` self-exemption checked first.

`PipelineOrderAssertion` (new file, project root, namespace `SharedKernel.ArchitectureTests`) —
plain reflection helper, NOT `ConditionList`/`ICustomRule`. Walks `IServiceCollection`
`ServiceDescriptor` entries for `ServiceType.Name == "IPipelineBehavior\`2"`, compares
`ImplementationType` (normalized via `GetGenericTypeDefinition()` if not already open) against a
caller-supplied `Type[]` sequence via `SequenceEqual`. Never calls `BuildServiceProvider()`.
Required adding `Microsoft.Extensions.DependencyInjection.Abstractions` (10.0.1, matching the
dominant pin across the repo) as a new package reference to `SharedKernel.ArchitectureTests.csproj`
— this package had never referenced DI abstractions before.

## Critical pitfall: NetArchTest excludes [CompilerGenerated] types entirely

Confirmed via direct Mono.Cecil IL inspection of `NetArchTest.Rules.dll` (`Types.GetAllTypes`):
the type-enumeration walk recurses into `TypeDefinition.NestedTypes` (so nested types ARE
normally visible) but explicitly filters out any type whose `CustomAttributes` contains
`System.Runtime.CompilerServices.CompilerGeneratedAttribute`. The C# compiler always marks
`async` state-machine structs/classes `[CompilerGenerated]`. Consequence: **any `await
SomeMethod()` call inside an `async` method is invisible to every IL-walk `ICustomRule`
predicate in this domain**, because the call instruction lives inside the generated
`<MethodName>d__N::MoveNext` nested type, which NetArchTest never scans.

Practical implication: when writing a fixture meant to prove an IL-walk predicate fires on a
call inside the *declaring* type's own method body (not a lambda/local-function/iterator/async
state machine), use the **synchronous** form of the call
(`Task.Delay(100).GetAwaiter().GetResult()`) instead of `await Task.Delay(100)`. The async form
will silently produce a false pass-path no matter how correct the predicate logic is. This is a
structural detection-surface limitation of NetArchTest itself (not specific to any one
predicate) — document it in any future fixture/predicate touching IL-level Call/Callvirt
detection of async-friendly BCL APIs (`Task.Delay`, `HttpClient.SendAsync`, etc.).

## Critical pitfall: GenericInstanceType.FullName includes the closed argument list

A `GenericParameter.Constraints[i].ConstraintType` for a closed-generic interface constraint
(e.g. `where TRequest : IStreamRequest<TResponse>`) has
`TypeReference.FullName == "MediatR.IStreamRequest\`1<TResponse>"` — NOT the bare open-generic
form `"MediatR.IStreamRequest\`1"`. A direct string-equality check against the open-generic name
will never match. Fix: when the `TypeReference` is a `Mono.Cecil.GenericInstanceType`, compare
against `((GenericInstanceType)typeReference).ElementType.FullName` instead, which IS the bare
open-generic form. Verified via a throwaway Mono.Cecil probe console app compiling the exact
constraint shape and dumping `FullName`/`ElementType.FullName`.

## MediatR test-fixture reference gotcha (historical — pre-WO-086)

Since WO-086 pipeline fixtures compile against the kernel contracts in `SharedKernel.Application`
(`IRequest<T>`, `IPipelineBehavior<,>`, `IStreamQuery<T>`), not MediatR. The note below only
applies to a fixture that deliberately exercises `SharedKernel.Application.Mediator.MediatR`.

`MediatR.IBaseRequest`/`IRequest<TResponse>`/`IStreamRequest<TResponse>` live in the separate
**MediatR.Contracts** assembly; `IPipelineBehavior<,>`/`RequestHandlerDelegate<TResponse>` live
in the main **MediatR** assembly. `typeof(MediatR.IBaseRequest).Assembly.Location` resolves to
`MediatR.Contracts.dll`, which does NOT carry `IPipelineBehavior<,>` — referencing only that
assembly in a `CompileInMemory` fixture helper produces `CS0246: 'IPipelineBehavior<,>' not
found` even though `MediatR.IBaseRequest` itself resolves fine. Always add BOTH
`typeof(MediatR.IBaseRequest).Assembly.Location` AND
`typeof(MediatR.IPipelineBehavior<,>).Assembly.Location` to the base reference list when a
fixture needs both vocabularies.

Also: `Assembly.Load("System.Threading.Tasks")` throws in .NET 10 (the facade assembly is not
present standalone in the runtime's deps) — use `typeof(System.Threading.Tasks.Task).Assembly`
instead, consistent with the existing `Assembly.Load("System.Console")` pattern already proven
to work in `ServiceDefaultsGovernanceRulesTests`.

## Test-project NuGet additions (test project only, never the production package)

Added to `SharedKernel.ArchitectureTests.Tests.csproj` only (versions are now central in
`Directory.Packages.props`):
- (historical) a direct `MediatR` `12.4.1` reference — gone since WO-086; the test project
  reaches MediatR only through a `ProjectReference` to `SharedKernel.Application.Mediator.MediatR`
  (P-567), and `DependencyGraphRulesTests.MediatR_IsReferencedOnlyByTheMediatorAdapter` forbids
  any other production reference. 00.Governance production packages are Tooling tier and
  reference nothing.
- `Microsoft.Extensions.DependencyInjection` `10.0.9` — NU1605 downgrade error forced this
  exact version (transitive floor from `SharedKernel.Caching.Redis.DistributedLocking` →
  `RedLock.net` → `Microsoft.Extensions.Logging` `10.0.9`). Always check transitive floors via
  the NU1605 error message before guessing a version for a multi-reference test project.

## Phase Key Registry / root Phase Backlog closure pattern confirmed working

Unlike the WO-027/WO-028 phases (see `ref_servicedefaults_governance_rules.md` /
`ref_string_constants_detector_pattern.md`), this phase's Phase Key Registry row in
`00.Governance/state-map.md` WAS present from the start, including a populated `Root Backlog ID`
column (`P-225`). Closing the root Phase Backlog entry `### P-225` (Status `◐` Dispatched → `●`
Complete) and the root Domain Summary Board row for `00` both required manual edits since this
WO-specific phase key does not map to a standard lifecycle phase name (`Design`/`Scaffold`/
`Core`/`Tests`/`Docs`/`Published`) — the Domain Summary Board's Current Phase column instead
tracks "the latest completed WO-specific phase name" by established convention (confirmed by
reading the prior row's content before this session, which named the previous
`SK.00.PresentationArchRules` phase).
