---
name: generic_result_failure_construction
description: How pipeline behaviors build a Result/Result<T> failure from Error when TResponse is generic — FailureResponse.Create<TResponse> in SharedKernel.Application.Pipeline
metadata:
  type: project
---

> WO-086 (2026-09): `SharedKernel.Application.Behaviors` is `SharedKernel.Application.Pipeline`; the Expression-compiled `FailureResponseFactory` described in earlier versions of this note was replaced by `Shared/FailureResponse.cs`.

Behaviors that short-circuit (`AuthorizationBehavior`, `ValidationBehavior`, `IdempotencyBehavior`) must return a failed `TResponse`, where `TResponse` is only known to satisfy `IRequest<TResponse>` — at runtime either the non-generic `Result` or a closed `Result<T>`.

**Current solution:** `SharedKernel.Application.Pipeline/Shared/FailureResponse.cs` (`internal static class FailureResponse`, `TResponse Create<TResponse>(Error error)`). `typeof(TResponse) == typeof(Result)` is a straight cast; any other `TResponse` resolves its public static `Failure(Error)` method once via `GetMethod`, binds it with `CreateDelegate`, and caches the delegate in a generic nested `Cache<TResponse>` class — one of the documented reflection sites in `05.Application/CLAUDE.md`. A response type without that factory throws `InvalidOperationException` at the first short-circuit (00.Governance's SK0040 flags a marker on a non-`Result` request at compile time).

**Why not `dynamic`:** it pulled in `Microsoft.CSharp` for no other reason and is DLR-based. Do not re-add it.

**How to apply:** a new behavior that needs "construct Result/Result<T> from Error generically" reuses `FailureResponse.Create<TResponse>(error)` — never a new mechanism, never `dynamic`.
