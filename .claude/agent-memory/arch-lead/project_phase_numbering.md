---
name: phase-numbering-state
description: Last known phase and work order numbers in the root state-map Phase Backlog
metadata:
  type: project
---

As of 2026-06-29, the last phase written to `state-map.md` Phase Backlog is **P-219** under **WO-035**.

Next new phase must be **P-220**. Next new Work Order must be **WO-036**.

**WO-035 context:** 05.Application first real build-out — P-214–P-219 (Design, Scaffold, Core split into P-216 Contracts / P-217 Behaviors, Tests, Docs+Published). Domain was `○ Not Started` so `state-map-phase` was called (now `◐ Design`). Accepted the existing five-behavior design verbatim (Validation/Logging/Metrics/Transaction/Caching — the latter carrying forward root P-015/WO-004 unchanged), upgraded by adding two new opt-in behaviors (Authorization, Idempotency) each backed by a locally-owned seam interface — never reaching past `05.Application`'s `01–04` layering ceiling into `12.Security`/`07.Messaging`, mirroring the existing `IUnitOfWork`/`TransactionBehavior` bridge precedent. Canonical pipeline order revised to seven steps. See [[project_wo035_application_buildout]] for full detail.

**WO-034 context (prior):** User questioned the just-shipped (WO-033) `SharedKernel.Cryptography` package on three fronts — move out of `01.Core`? split into `.Abstractions`+per-algorithm packages? generalize away from password-specific naming? Declined the first two (no infrastructure-provider axis to justify either move), accepted the third as an Upgrade — `IPasswordHasher`/`Pbkdf2PasswordHasher`/`PasswordVerificationResult` rename to a secret-agnostic contract, mechanism unchanged, caught before any consumer adopted the just-packed `1.0.0`. P-210–P-213, all in `01.Core` (already `●` Published, so no `state-map-phase` call — backlog-only). `sync-brain` deferred to P-213 when final names lock. See [[project_wo034_cryptography_generalization]] for full reasoning.

**WO-033 context (prior):** P-205–P-209 — added `SharedKernel.Cryptography` as a sixth `01.Core` package (now superseded in naming by WO-034's P-210 rename, mechanism/structure otherwise unchanged).

**WO-032 context:** 15.Integration first real build-out — P-200–P-204 (Design, Scaffold, Core split into P-202 Signing / P-203 Dispatch, combined Docs+Published). Domain was `○ Not Started` so `state-map-phase` was called (now `◐ Design`). Accepted a pre-drafted `15.Integration/CLAUDE.md` domain brain (`SharedKernel.Integration.Webhooks` — signed outbound webhook dispatch) with one upgrade: split the templated single Core phase into independently-verifiable Signing/Dispatch sub-phases, mirroring the WO-031 WebApi/SignalR split. No sync-brain needed — domain already documented in root CLAUDE.md. See [[project_wo032_integration_buildout]] for full detail.

**WO-031 context (prior):** 14.Presentation first real build-out — P-192–P-198 (full 6-phase lifecycle, Core split into P-194 WebApi / P-195 SignalR) plus P-199 (00.Governance ProblemDetails/Result-HTTP enforcement rules). Domain was `○ Not Started` so `state-map-phase` was called (now `◐ Design`); 00.Governance already `● Complete` so P-199 queued in backlog only. See [[project_wo031_presentation_buildout]] for full detail including the correlation-id/13.ServiceDefaults decoupling upgrade.

**How to apply:** Always read the current Phase Backlog before assigning new IDs — this memory is a starting point, not a substitute for reading the file. This note has drifted stale at least twice before (once at P-159/WO-025 when the real file was at P-174/WO-027; again at P-178/WO-028 when the real file was at P-187/WO-029) — always verify against `grep -n "^### P-" state-map.md | tail` and `grep -oE "WO-[0-9]{3}" state-map.md | sort -u | tail` before trusting this note's numbers.

**WO-030 context:** 16.Testing gold-standard audit, triggered by direct user request ("are we done, what's missing, does the rest of the repo need to adopt this package"). Two-part finding:
1. **16.Testing self-audit (P-188–P-190):** three capabilities documented in `16.Testing/CLAUDE.md` at `[STATUS: Planned]`/scope-locked-out but never built, verified against actual `.cs` files on disk (none exist):
   - P-188 `FakeUserContext`/`FakeTenantProvider` (`Security/`) — highest-leverage gap found; `IUserContext`/`ITenantProvider` are the most universally-consumed abstractions on the platform and have had zero shared fake since the Design phase, while every sibling capability (Caching, Domain, Messaging, Persistence, Communication, ServiceDefaults) was built and shipped in the same pass.
   - P-189 `FakerSeeding` (`Fakers/`) — the Bogus determinism convention this package's own Implementation Rules call "non-negotiable," documented in prose, never implemented as code.
   - P-190 `FakeOutboxStore`/`OutboxMessageFaker` (`Persistence/`) — explicitly scope-locked OUT at P-182 ("deferred to a future work order") because 06.Persistence's outbox capability hadn't shipped yet at the time; 06.Persistence has since reached `● Published` with a real outbox interceptor, so the original deferral justification no longer holds — this is "supersede a stale scope lock," not "introduce new scope."
2. **Repo-wide adoption audit (P-191):** grepped all 39 `*.Tests.csproj` files for a `ProjectReference` to `SharedKernel.Testing.csproj` — only 16 had it. Verified (not assumed) which of the 23 non-referencing projects were genuine gaps vs. legitimate exemptions:
   - Legitimate exemptions confirmed: `01.Core` sits below `16.Testing` in layering, can never reference it (by design — `SharedKernel.Primitives.Tests` correctly rolls its own private clock fake). `05.Application`, `08.Storage`, `09.Search`, `10.Intelligence`, `14.Presentation`, `15.Integration`, `17.Workflows` test projects are empty stub `.csproj` files (confirmed by reading file content, not by board state alone) — matches their `○ Not Started` board rows, nothing to retrofit.
   - Ruled OUT despite no `16.Testing` reference: `02.Caching.FusionCache.Tests`, `02.Caching.Redis.HashStore.Tests` — these test the REAL `FusionCacheService`/`RedisHashService` implementations against lower-level collaborators (`IDistributedCache`, `IConnectionMultiplexer`), not against `ICacheService` as an injected dependency. You don't fake the thing under test. No retrofit applies.
   - Confirmed REAL drift (P-191): `07.Messaging.Abstractions.Tests/ConsumerVerifyTests.cs` uses `Substitute.For<IMessageBus>()`/`Substitute.For<IEventPublisher>()` — hand-rolled NSubstitute stubs for the exact two interfaces `16.Testing`'s `InMemoryMessageBus`/`InMemoryEventPublisher` already fake, happening inside 07.Messaging itself.

**Key methodology note for future audits of "is package X done / does the repo use it":** grep csproj files for the actual ProjectReference, don't trust a domain's own CLAUDE.md "implemented" claims OR the Domain Summary Board's "Published" status as proof of either (a) completeness of every planned capability inside the domain, or (b) adoption by consumers outside it. Both can be true ("Published," 100% of dispatched phases done) while specific planned sub-capabilities remain stubs and real consumers nearby still duplicate what already exists. Cross-check `[STATUS: Planned]` / `SCOPE LOCK` markers against the filesystem directly.

**Domains touched in WO-030:**
- 16.Testing: already ● Published — P-188–P-190 queued in backlog only; no state-map-phase call made
- 07.Messaging: already ● Published — P-191 queued in backlog only; no state-map-phase call made

No root CLAUDE.md changes this pass (sync-brain skipped) — no new technology, package, or naming pattern introduced; this was gap-filling inside an already-documented package shape, not new architecture.

---

**WO-029 context (prior):** 16.Testing: 14 long-pending, never-dispatched phases consolidated into 9 (P-179–P-187), full Design→Published delivery in one pass, plus `SharedKernel.Testing.SelfTests` carve-out for standalone helpers with no owning consuming-domain interface. See [[project_wo023_caching_redis_topology]] and [[project_wo028_servicedefaults_audit]] for unrelated prior work-order context still relevant to other domains.
