# SharedKernel.Scheduling.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **`InMemoryScheduledJobRegistry` implements `SharedKernel.Scheduling`'s `IScheduledJobRegistry` without a clock, a
> hosted loop or a lock store: registrations are recorded, and your test fires each tick with `TriggerAsync`.**

| You get | So that |
| --- | --- |
| A real `IScheduledJobRegistry` | The production code that registers your jobs runs unchanged against it |
| `TriggerAsync(jobName, simulatedNowUtc, …)` | A tick happens when the test says so — never on wall-clock time |
| `BeginInFlight(jobName)` + `simulatedMisfire: true` | `OverlapPolicy` and `MisfirePolicy` are asserted deterministically |
| The job's command sent through your `ISender` | You assert what was dispatched, with a stub sender or a real pipeline |
| `Fired`/`Skipped`/`Misfired` + `ShouldHave…` assertions | Readable failures, no test framework required |

## Install

```xml
<PackageReference Include="SharedKernel.Scheduling.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only** — the `TestingNeverReferencedByProduction` architecture rule fails any
production project that references a testing package.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Scheduling` (and through it `SharedKernel.Application` for `ISender`/`ICommand`) |
| Namespaces | `SharedKernel.Testing.Scheduling` |

## Quick start

There is no `Add*` helper: construct the registry with the `ISender` your test uses and hand it to the code that
registers jobs. This test uses a small recording sender.

```csharp
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Streaming;
using SharedKernel.Primitives.Results;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Testing.Scheduling;
using Xunit;

public sealed record SendInvoiceReminders(DateTimeOffset AsOf) : ICommand;

public sealed class InvoiceReminderScheduleTests
{
    [Fact]
    public async Task Reminder_job_sends_its_command_with_the_fire_time()
    {
        var sender = new RecordingSender();
        var registry = new InMemoryScheduledJobRegistry(sender);

        registry.AddRecurring(
            "invoice-reminders",
            "0 0 2 * * ?",
            ctx => new SendInvoiceReminders(ctx.ScheduledFireTimeUtc),
            o => { o.MisfirePolicy = MisfirePolicy.FireOnce; o.OverlapPolicy = OverlapPolicy.Skip; });

        var at = new DateTimeOffset(2026, 9, 1, 2, 0, 0, TimeSpan.Zero);
        var result = await registry.TriggerAsync("invoice-reminders", at);

        Assert.True(result!.Value.IsSuccess);
        Assert.Equal(new SendInvoiceReminders(at), Assert.Single(sender.Sent));
        registry.ShouldHaveFired("invoice-reminders");
    }

    private sealed class RecordingSender : ISender
    {
        public List<object> Sent { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult((TResponse)(object)Result.Success());   // ICommand answers Result
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamQuery<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
```

`Result` is a struct, so `TriggerAsync` returns `Result?`: `null` means the tick was discarded and nothing was sent.

## How it works

- **Registration is validated like production** where it matters: job names are unique and non-blank, the command
  factory and `configure` delegate are required, and `configure` must set both `MisfirePolicy` and `OverlapPolicy`
  (`ArgumentException` otherwise).
- **A tick** builds a `ScheduledJobExecutionContext` (`JobName`, `ScheduledFireTimeUtc` and `ActualFireTimeUtc` both
  set to `simulatedNowUtc`, the job's `TenantScope`, the optional `fencingToken`), calls the command factory, and
  sends the command through the constructor's `ISender`. Its `Result` is returned and the job is recorded in `Fired`.
- **Misfire:** `simulatedMisfire: true` records the job in `Misfired`; `MisfirePolicy.Skip` then discards the tick,
  `FireOnce` and `RunImmediatelyThenReschedule` dispatch it once.
- **Overlap:** while a `BeginInFlight` handle is undisposed, `OverlapPolicy.Skip` records the job in `Skipped` and
  discards the tick.

Where it simplifies:

- The cron expression is only checked for non-blank; it is not parsed. The deferred `fireAtUtc` is stored but never
  used — the test chooses every fire time.
- `OverlapPolicy.Queue` and `Allow` both dispatch immediately; nothing is queued.
- No distributed lease is taken and `LockExpiry` is ignored. Pass `fencingToken` to test code that reads
  `ScheduledJobExecutionContext.FencingToken`.
- The command is sent directly, not through `ScheduledCommandJob<TCommand>`: no `SystemRequestContext` scope, no
  new correlation id, no job logging or telemetry. Test those against the real scheduler.
- The registry is not synchronized — use one instance per test and trigger ticks sequentially.

## Reference

### `InMemoryScheduledJobRegistry : IScheduledJobRegistry`

| Member | What it does |
| --- | --- |
| `InMemoryScheduledJobRegistry(ISender sender)` | The sender every fired command goes through |
| `AddRecurring<TCommand>(string jobName, string cronExpression, Func<ScheduledJobExecutionContext, TCommand> commandFactory, Action<ScheduledJobOptions> configure)` | Records a recurring job; returns the registry for chaining |
| `AddDeferred<TCommand>(string jobName, DateTimeOffset fireAtUtc, Func<ScheduledJobExecutionContext, TCommand> commandFactory, Action<ScheduledJobOptions> configure)` | Records a one-shot job; returns the registry |
| `TriggerAsync(string jobName, DateTimeOffset simulatedNowUtc, long? fencingToken = null, bool simulatedMisfire = false, CancellationToken cancellationToken = default)` → `Task<Result?>` | Simulates one tick; `null` when discarded. `InvalidOperationException` for an unknown job |
| `BeginInFlight(string jobName)` → `IDisposable` | Marks the job running until disposed |
| `Fired`, `Skipped`, `Misfired` | `IReadOnlyList<string>` of job names, in order, one entry per event |
| `ShouldHaveFired(string jobName, int times = 1)` | Exact fire count |
| `ShouldHaveSkipped(string jobName)`, `ShouldHaveMisfired(string jobName)` | At least one overlap skip / simulated misfire |
| `Reset()` | Clears registrations, in-flight markers and every record |

Assertions throw `InvalidOperationException` with a readable message.

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.Scheduling.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Scheduling/SharedKernel.Scheduling.Testing/SharedKernel.Scheduling.Testing.Tests)
and run a real `ISender` built with `AddSharedKernelApplication(…, app => app.UseMediatR())`. To send the triggered
command through the real pipeline behaviours, use
[`SharedKernel.Application.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Application/SharedKernel.Application.Testing/README.md);
pair it with [`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
for `FakeClock` (derive `simulatedNowUtc` from it) and `TestRequestContext`.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference it from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Rely on it to reject a malformed cron expression | Test cron syntax against the real `AddSharedKernelScheduling` registry | The fake never parses cron |
| Expect `OverlapPolicy.Queue` to defer the tick | Assert queuing against the real scheduler | The fake dispatches `Queue` and `Allow` immediately |
| Read `IRequestContext` inside the handler and expect `SystemRequestContext` | Test the job's caller context with the real scheduler | The fake opens no request-context scope |
| Share one registry across parallel tests | Create one per test | Its collections are not synchronized |
| Forget the `using` on `BeginInFlight` | Dispose the handle | The job stays "in flight", so with `OverlapPolicy.Skip` every later tick is discarded |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
