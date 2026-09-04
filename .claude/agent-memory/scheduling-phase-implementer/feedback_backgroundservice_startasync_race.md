---
name: backgroundservice-startasync-race
description: BackgroundService.StartAsync schedules ExecuteAsync via Task.Run and returns immediately — test harnesses must poll for a "started" signal, never assume synchronous startup ran
type: feedback
---

`Microsoft.Extensions.Hosting.BackgroundService.StartAsync` does **not** run `ExecuteAsync`'s
"synchronous-looking" prefix before returning. Verified by decompiling
`Microsoft.Extensions.Hosting.Abstractions` 10.0.0 with `ilspycmd` (not assumed from memory — this
matters, the intuitive assumption is wrong):

```csharp
public virtual Task StartAsync(CancellationToken cancellationToken)
{
    _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    _executeTask = Task.Run(() => ExecuteAsync(_stoppingCts.Token), _stoppingCts.Token);
    return Task.CompletedTask;
}
```

It wraps `ExecuteAsync` in `Task.Run(...)` and returns `Task.CompletedTask` immediately, before the
thread-pool work item has even started, let alone reached the derived class's first `await`.

**Why:** Any test that does `await hostedService.StartAsync(); /* manipulate a FakeClock or assert
state */` races the loop's own startup. If the fake clock advances before the background task's
synchronous prefix reads `IClock.UtcNow` (e.g. to compute an initial next-fire-time), that
computation silently uses the wrong instant and the test's expected behavior never happens — with no
exception, no visible failure signal beyond "the thing I expected never occurred." This produced 9
simultaneously-failing tests in `19.Scheduling` (all traced to this one root cause) before being
found via `ilspycmd` decompilation.

**How to apply:** Any test harness wrapping a `BackgroundService`-derived type (in this repo or
elsewhere) must poll for an explicit "started" signal — e.g. an `IsRunning` flag the derived class
sets at the very end of its own startup prefix — before the test proceeds to manipulate clock/state.
Wrap this into the harness's own `StartAsync` method so every test gets it automatically; don't rely
on individual tests remembering to poll. A short `Eventually.UntilAsync`-style polling helper (real
wall-clock polling, not a sleep, with a few-second timeout) is the right shape. This generalizes to
any future `BackgroundService`-based hosted-loop testing anywhere in the repo, not just
`19.Scheduling`.

See also [[scheduling-lock-composition-correction]] for a different bug this same debugging session
surfaced.
