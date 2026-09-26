---
name: ref_workflow_topology_pattern
description: SK0028/SK0029 (17.Workflows) — type-attribution/inheritance analyzer scoping technique, single-package WorkflowTopologyRules shape, WO-046 P-290 closeout
metadata:
  type: reference
---

## SK0028 — first analyzer scoped by type attribution/inheritance, not namespace

Every prior SK analyzer scopes its trigger via the `SyntaxNode.Parent` namespace-ancestor walk
(SK0001/SK0007/SK0013/SK0026/...). SK0028 (`NonDeterministicApiUsageInsideWorkflowAnalyzer`) is the
first to scope by TYPE ATTRIBUTION/INHERITANCE instead: a type is in scope if it carries `[Workflow]`
(resolved via semantic model to `Temporalio.Workflows.WorkflowAttribute`) OR has `WorkflowBase`
anywhere in its base-type chain — with `ActivityBase`/`[Activity]` checked FIRST as a hard, positive
exclusion (not merely "outside scope").

**Implementation technique that generalizes:** rather than registering on the type declaration and
walking its descendants (which requires manually skipping nested-type subtrees), register directly
on the LEAF node kinds you actually care about (`MemberAccessExpressionSyntax`,
`InvocationExpressionSyntax`, `ObjectCreationExpressionSyntax`/`ImplicitObjectCreationExpressionSyntax`,
`ConstructorDeclarationSyntax`) and, for each leaf node, walk UP via
`node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()` to find the nearest enclosing
type, then run the scope check on that. This automatically handles nested types correctly (a nested
non-workflow type's own leaf nodes resolve to ITS OWN nearest enclosing type, not the outer one) with
no extra bookkeeping. Reuse this pattern for any future attribution/inheritance-scoped rule.

**Avoiding double-reporting invocation targets:** when a `MemberAccessExpressionSyntax` handler and an
`InvocationExpressionSyntax` handler are both registered, skip the member-access handler when
`memberAccess.Parent is InvocationExpressionSyntax inv && inv.Expression == memberAccess` — otherwise
`Guid.NewGuid()` gets analyzed twice (once as a bare member access, once as the invocation target).

**Symbol resolution notes verified empirically:**
- `ITypeSymbol.Name`/`INamedTypeSymbol.Name` never includes generic arity — `ILogger<T>` and the
  non-generic `ILogger` both report `Name == "ILogger"`. Distinguish them via `Arity` (0 vs 1), not
  string parsing.
- `Task.Run`/`Task.Delay` are static methods on the NON-generic `Task` type regardless of the
  delegate's return type — `method.ContainingType.Name == "Task"` is correct, no generic-arity check
  needed.
- `ConfigureAwait` exists on `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>` — all four report
  `ContainingType.Name` as `"Task"` or `"ValueTask"` (arity-stripped), so a `Name is "Task" or
  "ValueTask"` check covers all four in one branch.

## SK0029 — single-shared-exemption-namespace shape (mirrors SK0013, not SK0026)

When a domain has exactly ONE owning package (17.Workflows has no sibling providers), a raw-client
constructor-injection rule needs only ONE exemption namespace prefix
(`"SharedKernel.Workflows.Temporal"`), not a per-client-type dictionary like SK0026 (which has THREE
owning packages: Qdrant/Milvus/SemanticKernel). For the arity-insensitive type match (`WorkflowHandle`
vs `WorkflowHandle<T>`), match on `Name` + `ContainingNamespace` only, never `Arity` — "any generic
arity" in a spec means don't gate on arity at all.

## WorkflowTopologyRules — first *TopologyRules class with no sibling-provider pair

`RedisTopologyRules`/`StorageTopologyRules`/`SearchTopologyRules`/`IntelligenceTopologyRules` all
carry an `AbstractionsHasNoThirdPartyDependencies` + `ProviderPackagesNeverReferenceEachOther` pair
because those domains split into `.Abstractions` + N sibling `.{Provider}` packages.
`WorkflowTopologyRules` (17.Workflows, single package) carries NEITHER — only two DOMAIN-SPECIFIC
predicates instead: `NoRawClientAccessorConsumptionInRepo` (ctor-param + field exact-name check, no
exemption — mirrors `NoEncryptionRotationJobInjectionPredicate`'s ctor scan but ALSO scans
`TypeDefinition.Fields`, which that predicate does not) and `NoHealthChecksDependencyInWorkflows`
(single narrow-term `NotHaveDependencyOn` check, mirroring `IntelligenceTopologyRules`'s
`NoHealthChecksDependencyAcrossIntelligencePackages` precedent). If a future single-package domain
needs a `*TopologyRules` class, this is the template — don't force the Abstractions/Siblings pair
onto a domain that doesn't have that shape.

## Per-domain layering methods — deleted (WO-086)

This phase also added `SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication`,
a fourteen-forbidden-term method in the same family as `SearchReferencesOnlyCoreAndContracts`/
`IntelligenceReferencesOnlyCoreAndContracts`. All of those numbered-layer methods were deleted in
P-574: `SharedKernel.Workflows.Temporal` is Adapter tier (tenant scope from
`SharedKernel.Execution.Tenancy.TenantScope`, commands through the kernel `ISender`, readiness via
`IReadinessProbe` "workflows"), and `eng/SharedKernelTiers.targets` (SKTIER000–006, build errors)
plus `DependencyGraphRulesTests` enforce what it may reference. Never write a per-domain
forbidden-term list again — declare the package's `<SharedKernelTier>` instead; only purity rules the
tiers cannot express belong in `SharedKernelLayeringRules`.

## This phase's build/test outcome — clean on first attempt

Unlike several prior SK.00.*Topology phases, this implementation had ZERO test-authoring pitfalls
(no const-folding surprise, no reference-assembly conflict, no NetArchTest type-discovery gap) — both
`dotnet build`/`dotnet test` passed clean on the very first run for both `SharedKernel.Analyzers.Tests`
(215/215, +30 from 185 baseline) and `SharedKernel.ArchitectureTests.Tests` (179/179, +9 from 170
baseline). The in-compilation-stub technique (declare `Temporalio.Workflows.WorkflowAttribute`,
`Temporalio.Activities.ActivityAttribute`, `SharedKernel.Workflows.Temporal.Authoring.WorkflowBase`/
`ActivityBase`, `SharedKernel.Primitives.IClock`, `Microsoft.Extensions.Logging.ILogger`/`ILogger<T>`
as plain in-compilation stubs) worked without any reference-assembly-version conflict, unlike the
`Microsoft.Extensions.Logging.Abstractions`/`System.Diagnostics.Activity` issues hit in SK0020/SK0021/
SK0022 — because none of these stub types collide with a real, differently-versioned BCL-adjacent
package the sandbox's default reference set already carries.

## Recurring task-count discrepancy (again)

Same pattern as SK.00.CommunicationArchRules/SK.00.ServiceDefaultsGovernance: this phase's own
Goal/Scope prose and Overall Progress row said "26 tasks," but the task table has always held 27 rows
(D-65 + C-118–C-122 [5] + T-233–T-252 [20] + DO-37 = 27). Always COUNT THE ACTUAL TASK TABLE ROWS
rather than trusting the phase's own prose/summary — this is now the third or fourth time this exact
class of off-by-one has appeared in a governance-arch-planner-authored phase spec.
