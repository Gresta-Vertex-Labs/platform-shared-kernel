# Platform.SharedKernel — State Map

> Living cross-domain board: what exists, what is open, and the next free IDs. Each domain's own `state-map.md` holds its package board and open phase keys. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## ID Counters

| Counter | Next free | Note |
| --- | --- | --- |
| Next phase id | P-638 | Highest used: P-637 (WO-090) |
| Next work order | WO-091 | Highest used: WO-090 |

Phases written by `arch-lead` take the next phase id; a user-directed phase without a work order still takes one, so no id is ever reused.

## Domain Summary Board

| # | Domain | Packages | Status | Open items |
| --- | --- | --- | --- | --- |
| 00 | [Governance](tools/Governance/state-map.md) | 3 (Tooling) + dev-only Benchmarks | ● | None |
| 01 | [Core](src/Foundation/state-map.md) | 13 (10 Foundation, 3 Adapter) | ● | None |
| 02 | [Caching](src/Infrastructure/Caching/state-map.md) | 7 (1 Abstractions, 6 Adapter) | ● | None |
| 03 | [Domain](src/Model/Domain/state-map.md) | 1 (Model) | ● | None |
| 04 | [Contracts](src/Model/Contracts/state-map.md) | 1 (Model) | ● | None |
| 05 | [Application](src/Application/state-map.md) | 4 (1 Abstractions, 3 Host) | ● | None |
| 06 | [Persistence](src/Infrastructure/Persistence/state-map.md) | 6 (1 Abstractions, 5 Adapter) | ● | None |
| 07 | [Messaging](src/Infrastructure/Messaging/state-map.md) | 5 (1 Abstractions, 4 Adapter) | ● | None |
| 08 | [Storage](src/Infrastructure/Storage/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 09 | [Search](src/Infrastructure/Search/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 10 | [Intelligence](src/Infrastructure/AI/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 11 | [Communication](src/Infrastructure/Communication/state-map.md) | 3 (Adapter) | ● | None |
| 12 | [Security](src/Hosting/Security/state-map.md) | 5 (1 Abstractions, 4 Host) | ● | None |
| 13 | [ServiceDefaults](src/Hosting/ServiceDefaults/state-map.md) | 7 (Host) | ● | None |
| 14 | [Presentation](src/Hosting/Presentation/state-map.md) | 6 (Host) + WebApi generator (Tooling) | ● | None |
| 15 | [Integration](src/Infrastructure/Integration/state-map.md) | 4 (1 Abstractions, 3 Adapter) | ● | None |
| 16 | [Testing](src/Testing/state-map.md) | 21 (Testing; 20 packable + `Testing.Internal`) | ● | None |
| 17 | [Workflows](src/Infrastructure/Workflows/state-map.md) | 1 (Adapter) | ● | None |
| 18 | [Idempotency](src/Infrastructure/Idempotency/state-map.md) | 3 (1 Abstractions, 2 Adapter) | ● | None |
| 19 | [Scheduling](src/Infrastructure/Scheduling/state-map.md) | 1 (Adapter) | ● | None |
| 20 | [Reporting](src/Infrastructure/Reporting/state-map.md) | 5 (1 Abstractions, 4 Adapter) | ● | None |
| — | [samples](samples/README.md) | `Shop`: one Aspire platform, 7 services (Catalog, Ordering, Inventory, Billing, Merchant, Notify, Reports) | ● | None |

Every package ships at one repo-wide version through the release train (`CONTRIBUTING.md`, "Versioning and releases"); nothing is released per package.

## Open Work

### WO-090 — Kernel 1.1.0 from service-template feedback (`docs/proposals/v1.1.0-service-template-feedback.md`, Part B)

Accept, with one upgrade (B9). Every item targets 1.1.0 and must leave a 1.0.0 consumer source- and behavior-compatible: each public-API change is additive and reviewed against the `PublicAPI` baseline, a changed default is opt-in or warned about rather than flipped, and shipped data shapes do not change. The items are observed defects or gaps from building the service template and running the consumer-skill acceptance tasks; none breaks a tier or purity rule, and none needs a new package, a new declared adapter edge or a root `CLAUDE.md` change. Upgrade (B9): the proposal's "register the OTLP exporter only when an endpoint is configured" would stop exporting for a deployment that relies on the implicit `localhost:4317`, which is a behavior break; 1.1.0 instead adds an explicit exporter mode whose default preserves 1.0.0, warns once at startup when the exporter is on with no endpoint configured, and lets a host (the template's test hosts, a pod without a collector) choose "only when an endpoint is configured". Flipping the default is a 2.0.0 candidate and is recorded as such. B2 note: the paged lists already carry a `JsonConstructor` on a non-public constructor, which a source-generated context in a consumer assembly cannot call; the planner confirms the actual failure before choosing the fix. Entries are ordered producers before consumers by tier, then folder. Edits to the consumer skill's needs table serialize through the `Depends on` chain below. The two `eng` entries are executed through `/devops`, not `/dispatch-phase`. Out of scope, as in the proposal: template changes, a smart-enum base type, a second thin template.

#### P-627 — Non-throwing business-rule check (B1)
**Status:** `●` Done
**Domain:** 03.Domain
**Depends on:** None
**Phase key:** SK.03.RuleResult
Behavior methods that return `Result` need a way to evaluate a business rule without throwing, today they either wrap the throwing check in `ResultTry` (about ten lines per method) or hand-write the broken-rule-to-error mapping. Add a non-throwing counterpart to the rule check, available wherever the throwing check is (aggregate roots, value objects, domain services), that returns a `Result` carrying the rule's own error and error type, so a service never reimplements the mapping. Purely additive: the throwing check and every existing behavior are unchanged.
- [ ] The non-throwing check exists on aggregate roots, value objects and domain services and returns a failure carrying exactly the error the throwing check's exception would carry (code, message, error type), and a success when the rule holds.
- [ ] The Domain README documents it next to the throwing check with the rule of thumb: constructors and factories use the throwing check, behavior methods that return `Result` use the new one; the snippet compiles against the public API.
- [ ] Tests cover broken and satisfied rules, composite rules, and parity of the error with the throwing path; the Domain consumer-verify harness exercises it.
- [ ] No 1.0.0 public member changes; the new members are recorded as unshipped public API.
- [ ] The testing fakes and the consumer skill mention it only if they reference the throwing check today.

#### P-628 — Paged lists deserializable through a source-generated JSON context (B2)
**Status:** `●` Done
**Domain:** 04.Contracts
**Depends on:** None
**Phase key:** SK.04.PagedListSourceGen
The offset-paged list cannot be read through a consumer-assembly `JsonSerializerContext`, so every service client falls back to reflection-based JSON for list results, an exception to the AOT preference. Make the offset-paged list, and the cursor-paged list if it has the same limit, deserializable through a source-generated context while keeping their validation (invalid documents still fail as a JSON error) and their fixed wire shape. The fix must not change any 1.0.0 public signature, the wire format, or the factory semantics.
- [ ] A source-generated context declared in a separate consumer-style assembly round-trips the offset-paged list (and the cursor-paged list if affected) for a representative item type, with a test or consumer-verify harness proving it.
- [ ] Reading a document that breaks a paging rule still fails with a JSON error, and the wire member names and computed members are unchanged.
- [ ] The public-API baseline shows additions only; no 1.0.0 member is removed or changes signature.
- [ ] The Contracts README documents how a service registers the lists in its context; the consumer skill is updated where it tells a client to use reflection for paged results.

#### P-629 — Caching recipe: name every required setting (B6)
**Status:** `●` Done
**Domain:** 02.Caching
**Depends on:** None
**Phase key:** SK.02.RecipeRequiredSettings
The caching recipe of the consumer skill wires caching without the service name the package marks as required, so a following AI run builds a host that fails at startup. Make the recipe list every required setting of each package it wires, say plainly that a missing one stops the host at startup, and show the wiring with the setting present. This is a documentation and recipe change; if verification shows a package's required setting is not validated at startup, say so to the caller instead of documenting a claim that is false.
- [ ] `add-caching` names the service name (and any other required setting of the packages it wires, Redis L2 included) in its package table or steps and in the code it shows.
- [ ] The recipe states that a missing required setting fails host start, and that statement is verified against the real validation.
- [ ] The `proven-by` Shop path still builds and still matches the recipe's wiring; consumer-skill checks stay green.

#### P-630 — Migrations of another package's DbContext on the migrator connection (B7, persistence side)
**Status:** `●` Done
**Domain:** 06.Persistence
**Depends on:** None
**Phase key:** SK.06.PackageContextMigration
A kernel package that ships its own `DbContext` (the Idempotency EF Core store) cannot be migrated safely under the database role split: a hand-written startup migration on the runtime connection fails with permission denied, and the supported migrate-on-startup mechanism applies a service's own context. Provide the supported way for another kernel package's context to be migrated on the migrator connection, under the same cross-replica serialization as the service's own migrations. If the existing seam already allows this without change, record the documented way and decline the code change with the evidence (a `⊘` with the reason is the correct outcome). Additive and opt-in; a service that created the schema by hand keeps working.
- [ ] A package-supplied context can be registered for migration on the migrator-role connection, running once across replicas and before the host reports ready, and is not applied on the runtime connection.
- [ ] Documented in the EfCore README next to `MigrateOnStartup`, including the grants the migrator role needs.
- [ ] Integration test against the real role split (runtime role cannot create the objects, migrator role can); no change to existing registrations' behavior.
- [ ] Anything Idempotency needs from this is stated in the phase so P-632 can depend on it.

#### P-631 — Recipe: publish and consume a service's Contracts and Client (B4)
**Status:** `●` Done
**Domain:** 11.Communication
**Depends on:** P-629
**Phase key:** SK.11.ServiceContractsRecipe
No consumer-skill recipe covers the pattern every service follows: publish a `Contracts` package (events and DTOs, additive changes only, versioned events) and a typed REST `Client` over the REST client package, and consume another service's client or event type behind the consumer's own port, registered in Infrastructure and never referenced from Application. Add that recipe with its needs-table rows, `proven-by` paths from the Shop where it does this and an `unproven` declaration with a reason where it does not. Separately, state in the skill that recipes decide packages and APIs while the service's own `CLAUDE.md` decides folders, as one line in the skill entry file and one in the new-service recipe, so the recipes' Shop-derived file placement does not override a service's folder convention.
- [ ] A recipe covers publishing Contracts (event naming and version rule, additive-only evolution), publishing a Client, and consuming a foreign client or event type behind a local port; code snippets compile against the public API.
- [ ] The needs table has rows for "publish my service's contracts and client" and "call or consume another service" that route to the recipe and the packages involved.
- [ ] Every proof claim is a `proven-by` Shop path that exists, or an `unproven` entry with a reason; the consumer-skill checks pass.
- [ ] The "recipes decide packages and APIs; the service's own `CLAUDE.md` decides folders" line is present in the skill entry file and in the new-service recipe.
- [ ] Consumer vocabulary only; the catalog regenerates without an unrouted package.

#### P-632 — Idempotency EF Core store: a supported way to create its schema (B7)
**Status:** `●` Done
**Domain:** 18.Idempotency
**Depends on:** P-630
**Phase key:** SK.18.EfCoreSchemaMigration
The EF Core idempotency store ships no migrations, and neither its README nor the idempotency recipe says how to create its schema when migrations run as the migrator role and the service runs as the runtime role; an AI run hand-wrote a startup migration on the runtime connection and failed with permission denied. Provide a supported mechanism (shipped migrations or a migrate-on-startup hook on the migrator connection, consistent with the Persistence EF Core mechanism from P-630) and document it. Additive and opt-in; a service that already created the table by hand keeps working and the schema is not changed in a way that breaks it.
- [ ] A service following the documented way gets the idempotency schema created by the migrator role and runs the store as the runtime role, proven by an integration test against the real role split.
- [ ] The package README and the `add-idempotency` recipe state the exact steps for both the supported mechanism and the manual fallback, with the required grants.
- [ ] Applying the mechanism to a database where the table already exists does not fail or alter data.
- [ ] The in-memory fake and the Shop's use of the store are unaffected, and the Shop build and its consumer-skill `proven-by` still hold.

#### P-633 — "Run on one replica only" routes to the distributed lock (B8)
**Status:** `●` Done
**Domain:** 19.Scheduling
**Depends on:** P-631
**Phase key:** SK.19.OneReplicaGuidance
A request to run a job on one replica used the scheduler correctly but skipped the Redis distributed lock the job recipe depends on, because the lock is mentioned but not prominent. Make single execution across replicas a first-class statement in the background-job recipe and give it its own needs-table row ("Run on one replica only" routes to Scheduling plus the Redis distributed-locking package). Where the scheduler can tell at startup that no distributed lock is registered while jobs are configured, it logs a warning rather than failing, so an existing single-replica service is not broken.
- [ ] `add-background-job` opens its steps with the one-replica guarantee and what provides it, and shows the lock wiring in the example.
- [ ] The needs table has a "Run on one replica only" row naming both packages and the recipe.
- [ ] If the missing-lock warning is feasible without a tier or dependency change, it is implemented with an own-sub-block log message, tested, and documented; otherwise the phase records why it is not and leaves the documentation change.
- [ ] No existing registration or job behavior changes; consumer-skill checks stay green.

#### P-634 — OTLP exporter mode in service defaults (B9, upgraded)
**Status:** `●` Done
**Domain:** 13.ServiceDefaults
**Depends on:** None
**Phase key:** SK.13.OtlpExporterMode
Service defaults always enable the OTLP exporter, so a host with no collector endpoint exports to the implicit local default and its shutdown waits out the exporter timeout (8 to 9 seconds per test host observed) while a pod without a collector keeps retrying. Add an explicit exporter mode with at least "always" and "only when an endpoint is configured" (the endpoint environment variable or the kernel's equivalent setting, matching how Aspire's own defaults behave). The 1.1.0 default stays "always" so no 1.0.0 deployment loses telemetry; when the exporter is on and no endpoint is configured, the host logs one startup warning that names the setting and the remedy. Record "default to only-when-configured" as a 2.0.0 candidate in the domain's `Known Limitations`/`Decisions`.
- [ ] With the mode set to "only when an endpoint is configured" and no endpoint present, no OTLP exporter is registered for traces, metrics or logs, and host shutdown does not wait on a collector; with an endpoint present it exports as before.
- [ ] With the default mode, behavior is identical to 1.0.0 (same registrations), plus the single startup warning when no endpoint is configured; a test pins both.
- [ ] The setting binds through the validated options pattern with a documented section path, is covered in the package README configuration table, and the Aspire local path is unchanged.
- [ ] The release-note text for the warning and the opt-in is stated in the phase's acceptance so the release notes carry it.

#### P-635 — Ship the service-shape and placement rules in `SharedKernel.ArchitectureTests` (B3)
**Status:** `●` Done
**Domain:** 00.Governance
**Depends on:** None
**Phase key:** SK.00.ServiceShapeRules
The rules that check a consuming service's project shape live only in the Shop's test-support project and are copied, then drift, into every generated service. Ship them as reusable rule factories in the existing architecture-tests package: the four-project service shape and the six-project shape (adding Contracts and Client), and the placement rules (handlers under per-use-case command and query namespaces, business rules under a rules namespace, EF configurations, consumers and endpoints under their conventional namespaces, no `Managers`/`Helpers`/`Utils`/`Extensions`/`Exceptions` namespace segments, no custom exception types). The package index the rules need (which tier a kernel package is) ships with the package and is generated from the same inventory as the package catalog, so it updates by version and CI fails when it is stale. The package stays Tooling tier (references nothing); the Shop's services then use the shipped rules instead of their local copy. Additive to the package's public API.
- [ ] Shape rules for both variants and the nine placement rules are public rule factories with documented parameters (project names or suffixes), each with a firing and a passing test; the rule-execution coverage check stays green.
- [ ] The kernel package index is embedded data generated from the package inventory, matches `docs/packages.md`, and a stale index fails the existing generated-view check; any change the generator needs is reported as an `eng` hand-off.
- [ ] The Shop's test-support project drops its local rule implementations for the shipped ones and the Catalog and Ordering architecture tests still pass.
- [ ] The package README documents both variants with a snippet and states that rules are opt-in, so no 1.0.0 consumer's tests change.
- [ ] No new tier edge: the package keeps referencing nothing in the kernel; the tier check and the dependency-graph rules stay green.

#### P-636 — Consumer-view vulnerable-package gate (B5)
**Status:** `○` Pending
**Domain:** eng
**Depends on:** None
**Phase key:** —
A vulnerable transitive dependency of a packed kernel package reaches every consumer and nothing in kernel CI catches it; it was found only by hand-restoring each package alone outside the repository with full NuGet audit. Add a CI gate, on pull requests and on a schedule, that restores every packed package as a consumer would (outside the repository, with `NuGetAuditMode=all`, from the packed output on pull requests and from the published feed on the schedule) and fails on any NU190x finding, naming the package and the advisory. A scheduled run is needed because advisories appear without any code change.
- [ ] The gate runs in the pull-request verification path and on a schedule, covers every packable project (the set comes from the package inventory, not a hand list), and fails the run on NU1901 to NU1904 with the offending package in the log.
- [ ] A known-bad probe (a package reference that pulls a vulnerable version) is shown to fail the gate once; the probe is not left in the shipped set.
- [ ] Interaction with the existing in-repo audit setting and the transitive-pin policy is documented, and the gate is recorded in the `eng/README.md` CI table; the aggregator stays the only required check.
- [ ] Every action is SHA-pinned and the workflow lint stays green.

#### P-637 — Release-time check that every published package is public (B10)
**Status:** `○` Pending
**Domain:** eng
**Depends on:** P-626, P-636
**Phase key:** —
A new package on GitHub Packages starts private, while consumers' CI reads the feed with the job token, which sees public packages only; one private package breaks every service's restore with a 401 or 404. There is no API to change visibility, only to read it. Add a check after publish in the release workflow, and on a schedule, that every packable package exists on the feed and has public visibility (read through the packages API), and that fails the run naming the package and the manual step (Package settings, Change visibility). Add that manual step to the new-package checklist in `CONTRIBUTING.md`.
- [ ] After a release publish, the check enumerates the packable set from the package inventory, verifies each is on the feed at the released version and public, and fails naming each offender and the exact manual remedy.
- [ ] The same check runs on a schedule and needs only the permissions the workflow declares; it is documented in the `eng/README.md` CI table.
- [ ] The new-package checklist in `CONTRIBUTING.md` includes making the package public, and the release section says the release is not complete until the check passes.
- [ ] A dry run against the current feed passes (all 104 public) and a simulated private package makes it fail.

## Blocked

None.
