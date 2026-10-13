# 05.Application — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Application` | Abstractions | ● | MediatR-free kernel contracts: `IRequest<T>`/`IRequestHandler<,>`, `ICommand`/`IQuery<T>`/`IStreamQuery<T>`, `ISender`, `IPipelineBehavior<,>`, `IRequestValidator<T>`, `IDomainEventHandler<T>`, `[RequirePermission]` and every pipeline marker. |
| `SharedKernel.Application.Pipeline` | Host | ● | `AddSharedKernelApplication(assemblies, app => …)` — one call, seams checked at host start, second call throws; fixed pipeline Tracing → Logging → Metrics → Authorization → Validation → query stage → command stage (`WithIdempotency()`, `WithAuditing()`, `WithTransactions()`); behaviors internal. |
| `SharedKernel.Application.Pipeline.Caching` | Host | ● | `app.WithCaching()` — `ICacheableQuery` caching and post-commit `IInvalidatesCache` eviction. |
| `SharedKernel.Application.Mediator.MediatR` | Host | ● | `app.UseMediatR()` — MediatR 12.4.1 (last MIT release) behind `ISender`; the only MediatR reference in the repository. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

| Domain | Direction | Item |
| --- | --- | --- |
| 01.Core | inbound | `ErrorCodes.Idempotency.StoreUnavailable` = `idempotency.store_unavailable` (additive, see Blocked); P-580 helpers (`RecordResult`, `RecordException`, `AddExceptionEvents`, `WellKnownOutcomes`, `WellKnownTagKeys.Outcome`, `TelemetryVersion.Of`, `IHasError`); P-584 makes `CommitOutcomeUnknownException` and `TransactionRolledBackException` `SharedKernelException`s, which gives the new transaction span their `error.code` (richer, not required) |
| 16.Testing | inbound | P-581 `ActivityRecorder`/`MetricRecorder`/`LoggerAssertions`; `ApplicationPipelineTestHarness` (this domain) migrates to them in SK.05.ApplicationConformance |
| 18.Idempotency | outbound / coordination | `IIdempotencyStore` remarks say an unreachable store "throws"; P-615 should have the Redis and EfCore providers throw a `SharedKernelException` carrying `Error.Unavailable` with an `idempotency.*` code (this phase returns that `Error` unchanged) and may add a fault switch to `FakeIdempotencyStore` in `Idempotency.Testing`; nothing in this domain waits for it, and the catch-all for other exceptions stays as the safety net for third-party stores |
| 13.ServiceDefaults | outbound | P-582 deletes `WithApplicationTelemetry` and replaces its duration view with one seconds-histogram view for `SharedKernel.*`; the Application meter and source names are unchanged. Dashboards keyed on `request.type`/`request.kind`/cache instruments need the renames (release notes) |
| 14.Presentation | outbound | A failed `Result` with `idempotency.store_unavailable` (Unavailable) maps to 503 through the existing `ErrorType` map; its tests may add the code to the idempotency row |
| 00.Governance | outbound | `ApplicationPipelineRules`/`PipelineOrderAssertion` assert the request-behavior order only; the stream chain gains `StreamObservabilityBehavior` before `StreamAuthorizationBehavior` (verify the assertion does not enumerate `IStreamPipelineBehavior<,>`); P-583 report-only tests must show zero Application findings for SK.05.ApplicationConformance |
| 17.Workflows / 19.Scheduling | outbound | `CommandActivity<>`/`ScheduledCommandJob<>` send through `ISender` and now see an Unavailable `Result` instead of a raw store exception for an idempotent command; no code change expected |
