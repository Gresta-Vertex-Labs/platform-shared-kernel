---
name: scheduling-lock-composition-correction
description: Distributed-scheduling lock must be keyed per occurrence and never released early — a job-name-only acquire/release lock allows cross-replica duplicate firing
type: project
---

> WO-086 (2026-09): `IFencedLock` is now a self-expiring `IDistributedLockService.TryAcquireLeaseAsync` lease (`SharedKernel.Caching.Abstractions`); the per-occurrence, never-released design below is what shipped, with the lease's fencing token exposed as `ScheduledJobExecutionContext.FencingToken`.

While implementing `19.Scheduling`'s P-464 (`SharedKernel.Scheduling`), the arch-planner's original
design for cross-replica single execution ("acquire an `IFencedLock` per job per tick, keyed by job
name, release after the job body completes") turned out to be a genuine correctness bug, found while
designing the multi-replica proof test (not caught by code review alone — it only became obvious when
reasoning through the actual timeline two replicas would race through).

**Why:** if the lock key is just the job name, and the winning replica releases it immediately after
its (usually fast) execution finishes, a second replica evaluating the *same due occurrence* slightly
later — bounded only by inter-replica clock/tick-loop skew, which nothing tightly guarantees — finds
the lock already free and re-acquires it, firing the same occurrence a second time. This defeats the
entire point of the lock.

**The fix:** key the lock per **occurrence** (job name + the occurrence's own scheduled fire time,
e.g. `scheduling:occurrence:{jobName}:{scheduledFireTimeUtc.UtcTicks}`), and never release it
early — treat it as a claim that expires naturally via its configured TTL, not a
critical-section mutex. Once claimed, an occurrence's key can never be legitimately re-claimed by
another replica within the TTL window, closing the race regardless of how fast the winning replica's
execution finishes. This is the standard pattern real distributed schedulers use (Quartz's clustered
`JobStore`, Hangfire, db-scheduler) for exactly this reason — recognizing it as a known pattern (not
inventing something novel) made the fix easy to justify with confidence once the bug was spotted.

**How to apply:** whenever building or reviewing a distributed "exactly once across replicas"
mechanism backed by a plain mutex-shaped distributed lock (acquire/release), check whether the lock
key uniquely identifies the *occurrence* being protected, not just the *resource class*. A
resource-class-only key with early release is a duplicate-execution bug waiting for a timing window;
the tell is usually "how fast could two racing attempts land on either side of a release," which is
often much tighter than intuition suggests under real production conditions (GC pauses, network
jitter, thread-pool scheduling delays).

See also [[backgroundservice-startasync-race]] — a different bug found in the same debugging session,
while building the test that would have caught this one.
