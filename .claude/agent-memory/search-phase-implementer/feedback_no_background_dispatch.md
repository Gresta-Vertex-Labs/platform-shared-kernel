---
name: feedback_no_background_dispatch
description: Background Agent dispatch in this harness is slow to report, not lossy — do not assume "nothing landed" means "nothing will land," but also do not idle-wait on it for accountability-critical work
type: feedback
---
> WO-086 (2026-09): `ISearchIndexProvisioner.ProbeAsync` was removed — readiness is one `IReadinessProbe` per registered index (`SearchIndexReadinessProbe`, `search-{provider}-{index}`); the search-local `TenantScope` (string-keyed `TenantScope.Of(...)`) is now the single `SharedKernel.Execution.Tenancy.TenantScope` (`Global`, `For(TenantId)`, `FromNullable`); container fixtures live in `16.Testing/SharedKernel.Testing.Internal/Containers/` and the in-memory fakes in `SharedKernel.Search.Testing`. The findings below are history.

**Corrected understanding (2026-07-20, same SK.09.Tests real-backend session, after the original version
of this memory was written mid-session):** the original version of this memory claimed background `Agent`
dispatch "loses work" in this harness. That diagnosis was wrong. What actually happened: I dispatched two
background agents (Meilisearch T-13–T-17, ElasticSearch T-21–T-25) and ended my turn reporting them as
"running... standing by." The coordinator checked moments later, found zero files on disk, and corrected
me — accurately, for that moment in time. I then redid both providers' real-backend test suites myself,
synchronously, in the same session. Partway through, file-write conflicts ("modified since read, either by
the user or by a linter") started appearing — these were NOT the user or a linter; they were the two
original background agents, still running the whole time, progressively saving their own work. The
ElasticSearch agent finally delivered its completion notification **2,558,853 ms (~42.6 minutes)** after
dispatch, with a result matching what I had independently produced by then (same production bug class
found on the ES side — `ElasticSearchIndexProvisioner.ProbeAsync`'s index-scoped `HealthRequest` — same
ordering/tenant-exclusion evidence). The work was never lost. It was slow — real Testcontainers startup
plus writing and iterating a full real-backend suite against a live engine genuinely takes tens of minutes
— and the notification arrived long after a coordinator-visible "did anything happen" check would show
nothing.

**Why this matters:** two failure modes are both real and both worth guarding against, and they pull in
opposite directions:
1. Reporting a background dispatch as if it were a completed deliverable, then ending the turn — this
   makes "still running" look identical to "done" from the outside, which is exactly what triggered the
   coordinator's (reasonable, evidence-based) correction.
2. Redoing the same work synchronously without checking whether a background agent already dispatched for
   that exact task is still alive and will eventually deliver — this produces racing file writes,
   `Read`-before-`Edit` conflicts, and duplicated investigation effort (in this session: the ES background
   agent and I independently discovered variants of the same SDK/engine quirks, at real cost in tokens and
   wall-clock time on both sides).

**How to apply:**
- Never report a background dispatch's *existence* as if it were its *result*. The turn's deliverable is
  verified-on-disk, tests-green state — not "agents are running."
- If a coordinator/user reports "nothing landed" shortly after a background dispatch for genuinely
  slow work (real container integration, large multi-file test suites), consider that the agent may
  simply not have finished yet before concluding it failed silently — but do not idle-wait either;
  accountability-critical work (the kind this agent identity is on the hook for at session end) should
  proceed inline/synchronously regardless, per the coordinator's actual instruction, since a still-running
  background agent's eventual output is not addressable or interruptible mid-flight from a "wait for it"
  posture (see [[feedback_no_planning_mode]]-adjacent auto-mode guidance: keep moving, don't stall).
- If redoing work inline while a same-task background agent might still be alive, expect
  "file modified since read" conflicts as a SIGNAL, not noise — re-read before every edit (already
  mandatory tool behavior) and treat a surprising already-present, well-formed file as likely the
  background agent's own progress, not a bug to fight; verify it (build + test) before assuming it needs
  rewriting, since duplicating already-correct work wastes far more effort than validating it.
- The two agents in this instance used their own `search-phase-implementer` persistent memory too — check
  for a freshly-written sibling memory file (e.g. `project_09search_tests_realbackend_elasticsearch.md`)
  before re-deriving findings a concurrently-running instance already recorded.
