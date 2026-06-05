# 07.Messaging — State Map

> **What this file is:** Phase and task tracker for all work within `07.Messaging`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.07.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
| --- | --- | --- |
| `SK.07.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.07.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.07.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.07.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.07.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.07.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement IMessageBus | SK.07.Core | SharedKernel.Messaging.Abstractions | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.07.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Messaging.Abstractions` | — | `○` | Not started; zero transport dependencies; IMessageBus + IEventPublisher interfaces |
| `SharedKernel.Messaging.MassTransit` | — | `○` | Not started; MassTransit 8.x wiring; RabbitMQ + ASB transports; EF Core outbox integration |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.07.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Error`, `IClock`) | Available |
| `SK.07.Core` | `04.Contracts` | `SharedKernel.Contracts` ProjectReference (`EventEnvelope<TEvent>` — used in `MassTransitEventPublisher`) | Available |
| `SK.07.Core` | `03.Domain` | `DomainEventVersionHelper.GetVersion(Type)` — used to populate `SchemaVersion` in published envelope | Available |

---

## Phase: Design <!-- phase-key: SK.07.Design -->

> Finalize all interface shapes, builder API, transport option contracts, outbox wiring, CloudEvents envelope mapping, and consumer base semantics before implementation begins.

_No tasks defined yet._

<!--
| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| D-01 | Example task | WO-XXX | SharedKernel.Messaging.Abstractions | ○ |
-->

---

## Phase: Scaffold <!-- phase-key: SK.07.Scaffold -->

> Create project files, solution folder registrations, directory structure, and empty stub test files.

_No tasks defined yet._

<!--
| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| S-01 | Create SharedKernel.Messaging.Abstractions.csproj | WO-XXX | SharedKernel.Messaging.Abstractions | ○ |
-->

---

## Phase: Core <!-- phase-key: SK.07.Core -->

> Implement all production types: IMessageBus, IEventPublisher, PublishContext, MessagingOptions, IMessagingBuilder, ConsumerBase\<T\>, MassTransitMessageBus, MassTransitEventPublisher, MessagingBusBuilder, transport adapters, retry, and outbox wiring.

_No tasks defined yet._

<!--
| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| C-01 | Implement IMessageBus interface | WO-XXX | SharedKernel.Messaging.Abstractions | ○ |
-->

---

## Phase: Tests <!-- phase-key: SK.07.Tests -->

> Unit and integration tests for all packages. IMessageBus/IEventPublisher mocks, ConsumerBase retry/error semantics, outbox round-trip, CloudEvents envelope validation, MessagingBusBuilder guard tests.

_No tasks defined yet._

<!--
| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| T-01 | IMessageBus mock — verify PublishAsync called with correct type | WO-XXX | SharedKernel.Messaging.MassTransit | ○ |
-->

---

## Phase: Docs <!-- phase-key: SK.07.Docs -->

> Ensure all public types carry XML doc comments. Update CLAUDE.md with any implementation-phase discoveries. Write README.md for each package.

_No tasks defined yet._

<!--
| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| DO-01 | XML doc on IMessageBus, IEventPublisher, PublishContext | WO-XXX | SharedKernel.Messaging.Abstractions | ○ |
-->

---

## Phase: Published <!-- phase-key: SK.07.Published -->

> Set NuGet metadata, pack, verify manifests, and publish to internal feed.

_No tasks defined yet._

<!--
| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| P-01 | Set PackageVersion + PackageReleaseNotes in Abstractions.csproj | WO-XXX | SharedKernel.Messaging.Abstractions | ○ |
-->

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
| --- | --- | :---: | :---: | :---: | :---: |
| `SK.07.Design` | Design | 0 | 0 | 0 | `○` |
| `SK.07.Scaffold` | Scaffold | 0 | 0 | 0 | `○` |
| `SK.07.Core` | Core | 0 | 0 | 0 | `○` |
| `SK.07.Tests` | Tests | 0 | 0 | 0 | `○` |
| `SK.07.Docs` | Docs | 0 | 0 | 0 | `○` |
| `SK.07.Published` | Published | 0 | 0 | 0 | `○` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-06-05] Sub state-map initialized — phase key registry, 6 phases scaffolded at ○, no tasks yet; package board with 2 packages at not-started
