---
name: wo054-p352-messaging-testing-status
description: WO-054/P-352 (16.Testing Messaging/ PublishContext capture + propagator surface) status as of 2026-08-04 — T-72/T-73 done, T-74 genuinely blocked pending 07.Messaging
type: project
---

WO-054/P-352 extends the EXISTING `Messaging/` folder in `16.Testing` (`InMemoryMessageBus`/`InMemoryEventPublisher` gain `PublishContext` capture + `IMessageHeaderPropagator` application). As of 2026-08-04:

- `SK.16.Design`/`SK.16.Scaffold` — `●` complete (pure design/verification, no code).
- `SK.16.Core` (C-106/C-107) — `●` complete. Both fakes gained an optional `IEnumerable<IMessageHeaderPropagator>? propagators = null` ctor param; new `ShouldHavePublishedContext<T>()`/`ShouldHaveSentContext<T>()`/`ShouldHaveRequestedContext<TRequest,TResponse>()` (bus) and `ShouldHavePublishedContext<TEvent>()` (publisher) accessors return a **reference** to the real captured `PublishContext` instance, not a copy — this is why `.TenantId`/`.PartitionKey` will need zero further code once `07.Messaging` ships them.
- `SK.16.Tests` (T-72/T-73) — `●` complete, done in this session (2026-08-04). Extended `Messaging/InMemoryMessageBusTests.cs` (+12 tests, 13 pre-existing untouched) and `Messaging/InMemoryEventPublisherTests.cs` (+7 tests, 9 pre-existing untouched) — 19 new tests total, additive only. Full suite 889/889 (870 pre-existing + 19 new), 0 regressions, real Docker daemon incl. `Containers/`.
- **T-74 remains `⚑` Blocked** — genuinely, not stale. Re-verified directly on disk 2026-08-04: `07.Messaging.Abstractions/EventPublisher/PublishContext.cs` still declares only `CorrelationId`/`CausationId`/`Headers` — no `TenantId`/`PartitionKey`/`WithTenantId`/`WithPartitionKey`. `07.Messaging`'s own P-340/P-344 are still design-only in that domain's state-map. Do not implement T-74 until those ship — check `PublishContext.cs` directly, never trust either domain's prose.
- `SK.16.Docs` (DO-36) remains `○` Pending — the only other open item besides blocked T-74.

**Overall `SK.16.Tests` phase state is `⚑` (73/74), not `●`** — does NOT promote to root via the state-map-phase promotion condition (requires ALL tasks `●`). Root `state-map.md` domain-16 row `Current Phase`/`State` stay at `Docs`/`◐` per the established "never regress Current Phase" convention; only the `Summary: Next` text was corrected to stop saying "T-72–T-74" and instead name only T-74+DO-36 as remaining.

**Next actionable work**: DO-36 (Docs) can be done now — it's not blocked (just needs to document what's already shipped/tested, and explicitly mark T-74's scope as blocked in CLAUDE.md, which is already done). T-74 itself must wait for `07.Messaging` P-340/P-344.

See also [[domain_16_testing_conventions]] for the general blocked-vs-pending distinction and SelfTests routing rules this phase followed.
