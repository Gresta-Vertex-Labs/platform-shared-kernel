---
name: seven_step_pipeline_implementation
description: Notes from implementing 05.Application Core phase (WO-035) — package layout, ApplicationBehaviorsBuilder, key file locations
metadata:
  type: project
---

WO-035 Core phase (`SK.05.Core`, root P-216/P-217) implemented the full `SharedKernel.Application`
and `SharedKernel.Application.Behaviors` production surface in one session (2026-06-29).

**Key file locations** (for resuming/extending without re-deriving from CLAUDE.md each time):
- `05.Application/SharedKernel.Application/Messaging/` — `ICommandBase`, `ICommand`, `ICommandT.cs`
  (file named `ICommandT.cs` not `ICommandOfT.cs`, holds `ICommand<TResponse>`), `IQuery.cs`,
  `ICommandHandler.cs` (both arities), `IQueryHandler.cs`.
- `05.Application/SharedKernel.Application/DomainEvents/` — `IDomainEventHandler.cs`,
  `DomainEventNotification.cs`, `DomainEventNotificationHandler.cs` (internal),
  `MediatRDomainEventDispatcher.cs`.
- `05.Application/SharedKernel.Application/Extensions/ApplicationServiceCollectionExtensions.cs` —
  `AddSharedKernelApplication`, `AddDomainEventHandler<,>`.
- `05.Application/SharedKernel.Application.Behaviors/{Validation,Logging,Metrics,Transaction,
  Caching,Authorization,Idempotency}/` — one behavior + its seam interfaces per folder, matching
  CLAUDE.md's folder layout exactly.
- `05.Application/SharedKernel.Application.Behaviors/Shared/FailureResponseFactory.cs` — new in
  this phase, not pre-documented in CLAUDE.md before implementation; see
  [[generic_result_failure_construction]].
- `05.Application/SharedKernel.Application.Behaviors/Extensions/ApplicationBehaviorsBuilder.cs` +
  `ApplicationBehaviorsServiceCollectionExtensions.cs` — builder pattern with bool flags per
  behavior, `Build()` registers in fixed order regardless of call order via `services.AddTransient(
  typeof(IPipelineBehavior<,>), typeof(XBehavior<,>))`.

**csproj package additions beyond the Scaffold phase's initial set:**
- `SharedKernel.Application.csproj` needed `Microsoft.Extensions.DependencyInjection.Abstractions`
  10.0.1 added (Scaffold only wired MediatR + project refs; DI extensions needed this).
- `SharedKernel.Application.Behaviors.csproj` needed `Microsoft.Extensions.DependencyInjection.
  Abstractions` 10.0.1, `Microsoft.Extensions.Logging.Abstractions` 10.0.0 (for `LoggingBehavior`'s
  `ILogger<T>`), and `Microsoft.CSharp` 4.7.0 (for `dynamic` in `FailureResponseFactory`).

**MediatR API note:** `MediatR.IPublisher.Publish` signature in 12.4.1 is
`Task Publish(object notification, CancellationToken cancellationToken = default)` — works fine for
`MediatRDomainEventDispatcher`'s cached delegate without needing the generic `Publish<TNotification>`
overload.

**Phase scope confirmed:** despite the user-facing task list separating "Behaviors" tasks as
C-09..C-17, the actual CLAUDE.md design already included Authorization/Idempotency in the
seven-step canonical pipeline from the start (WO-035/P-214 design), so Core phase implements all
seven behaviors, not five — don't defer Authorization/Idempotency to a later phase.

Build was 0 warnings/0 errors on both packages after implementation. Tests are explicitly out of
scope for this phase (next phase is `SK.05.Tests`).
