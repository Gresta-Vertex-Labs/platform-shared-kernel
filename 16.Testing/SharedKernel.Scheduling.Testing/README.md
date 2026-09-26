# SharedKernel.Scheduling.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**`InMemoryScheduledJobRegistry` implements `SharedKernel.Scheduling`'s `IScheduledJobRegistry` without a clock, a
hosted loop or a lock store.** Registrations are recorded; the test fires a tick explicitly with `TriggerAsync`, and
the job's command is sent through the `ISender` you give it — so misfire and overlap policies can be asserted
deterministically.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Scheduling.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Scheduling`.

## Behaviour

| Member | What it does |
| --- | --- |
| `AddRecurring<TCommand>` / `AddDeferred<TCommand>` | Record the job, its command factory and its `ScheduledJobOptions`. As in production, `MisfirePolicy` and `OverlapPolicy` must both be set and job names are unique (`ArgumentException` otherwise). Nothing runs |
| `TriggerAsync(jobName, simulatedNowUtc, fencingToken?, simulatedMisfire?)` | Simulates one tick: builds the command from a `ScheduledJobExecutionContext` carrying `simulatedNowUtc` and the optional fencing token, and sends it. Returns the command's `Result`, or `null` when the tick was discarded |
| `BeginInFlight(jobName)` | Marks the job as running until the handle is disposed, so the next `TriggerAsync` exercises its `OverlapPolicy` (`Skip` records the job in `Skipped`) |
| `simulatedMisfire: true` | Treats the tick as a missed occurrence: recorded in `Misfired`; `MisfirePolicy.Skip` discards it, the other policies dispatch it |
| `Fired`, `Skipped`, `Misfired` | Job names in order, one entry per event |
| `ShouldHaveFired(jobName, times)`, `ShouldHaveSkipped`, `ShouldHaveMisfired` | Assertions that throw `InvalidOperationException` with a readable message |
| `Reset()` | Clears registrations and recorded events |

Real elapsed time never matters: no timer is started. The cross-replica lease is not simulated; pass a
`fencingToken` to test code that reads `ScheduledJobExecutionContext.FencingToken`. The fake sends the command
directly, so the production runner's request-context scope (`SystemRequestContext` with the job's tenant and a new
correlation id) is not opened — test that with the real scheduler.

## Registration

There is no `Add*` helper: construct it with the `ISender` your test uses (a hand-written double, or a pipeline
built with `SharedKernel.Application.Testing`) and pass it to the code that registers jobs.

```csharp
var registry = new InMemoryScheduledJobRegistry(sender);
```

## Example

```csharp
var registry = new InMemoryScheduledJobRegistry(sender);
NightlyJobs.Register(registry);                      // the production registration code under test

var at = new DateTimeOffset(2026, 9, 1, 2, 0, 0, TimeSpan.Zero);
await registry.TriggerAsync("invoice-reminders", at);

using (registry.BeginInFlight("invoice-reminders"))
{
    var result = await registry.TriggerAsync("invoice-reminders", at.AddDays(1));
    result.Should().BeNull();                        // OverlapPolicy.Skip discarded the tick
}

registry.ShouldHaveFired("invoice-reminders", times: 1);
registry.ShouldHaveSkipped("invoice-reminders");
```

## Related packages

- References `SharedKernel.Scheduling` (and through it `SharedKernel.Application` for `ISender`/`ICommand`).
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — `FakeClock`, `InMemoryLogger`, `TestRequestContext`.
- [`SharedKernel.Application.Testing`](../SharedKernel.Application.Testing/README.md) — a pipeline harness to send
  the triggered command through real behaviours.
