---
name: project-phase-p236
description: Locked design for P-236 (WO-039): IFailureFactory<TSelf> self-referential (CRTP) failure-factory contract in SharedKernel.Primitives
metadata:
  type: project
---

> WO-086 (2026-09): `SharedKernel.Application.Behaviors` is now `SharedKernel.Application.Pipeline` (Host tier, kernel-owned pipeline, no MediatR); `FailureResponseFactory`/`ResultOfTDispatcher` were deleted. `IFailureFactory<TSelf>` still exists in `SharedKernel.Primitives`.

P-236 adds one AOT-clean, self-referential interface to `SharedKernel.Primitives` so `05.Application` (P-237) can
genuinely eliminate the reflection its `FailureResponseFactory`/`ResultOfTDispatcher<TResponse>` was supposed to have
removed after [[project_phase_p230]] shipped `IResultOfT<T>`.

**Why:** Reading the shipped `05.Application` code (`05.Application/SharedKernel.Application.Behaviors/Shared/FailureResponseFactory.cs`)
showed `ResultOfTDispatcher<TResponse>.BuildFactory` still calls `Type.GetInterfaces()`, `Type.MakeGenericType()`,
`Type.GetMethod()`, and `MethodBase.Invoke()` to locate and invoke `Result<T>.Failure` — genuine reflection, cached
per closed `TResponse` but never eliminated. Invisible to `00.Governance`'s SK0012 rule because that rule matches only
the literal `MakeGenericMethod` IL call target, not `Type.MakeGenericType`/`GetMethod`/`Invoke`. `IResultOfT<T>` can't
fix this by itself because it's parameterized on the inner value type `T`, not on itself — a caller holding only the
open generic `TResponse` still has no way to name `Result<T>.Failure` without discovering `T` first (which is exactly
what the reflection was doing).

**How to apply:** If any future phase needs reflection-free construction of an unknown closed generic's instance from
just the open generic type parameter (not just reading an already-known-shape value), point to this CRTP pattern:
a self-referential interface with a C# static abstract member, `where TSelf : IInterface<TSelf>`, implemented by the
closed type in terms of itself. This is a distinct problem shape from `IHasSuccessFlag`/`IResultOfT<T>` (which read an
already-typed value) — don't conflate "read a known shape" seams with "construct an unknown shape" seams.

## Locked contract

`IFailureFactory<TSelf>` (self-referential/CRTP interface, `where TSelf : IFailureFactory<TSelf>`)
- `static abstract TSelf Failure(Error error)` — the only member
- Implemented by: `Result<T>` ONLY, as `IFailureFactory<Result<T>>` — via its **pre-existing** `Failure(Error error)`
  static factory (P-001/C-01). No new member added to `Result<T>`; the old method now also satisfies the interface.
- `Result` (non-generic struct) does **NOT** implement this — same exclusion rationale as `IResultOfT<T>`: callers
  needing a non-generic `Result` failure keep using the `TResponse == typeof(Result)` fast path in the consuming
  dispatcher (`05.Application`'s `FailureResponseFactory.Create<TResponse>`).
- No `[RequiresUnreferencedCode]` — hard constraint, same bar as `IHasSuccessFlag`/`IResultOfT<T>`.
- Additive only — `Result<T>`'s implements clause grows to `IHasSuccessFlag, IResultOfT<T>, IFailureFactory<Result<T>>`;
  no existing member signature on `Result<T>` or `Result` changes.

## Task IDs (SK.01.P236)
- D-28: IFailureFactory<TSelf> design
- C-41: IFailureFactory<TSelf> implementation (apply to Result<T> only)
- T-31: generic-constraint dispatch test, ≥2 distinct closed Result<T> shapes, zero System.Reflection in the path
- T-32: Result (non-generic) NOT assignable to IFailureFactory<Result>
- DO-14: XML doc IFailureFactory<TSelf>

## Cross-domain unlock
`05.Application` P-237 (WO-039) consumes this to rewrite `ResultOfTDispatcher<TResponse>.BuildFactory` as a direct
`TResponse.Failure(error)` call under `where TResponse : IFailureFactory<TResponse>`, deleting the
`Type.GetInterfaces()`/`MakeGenericType()`/`GetMethod()`/`Invoke()` bridge entirely. P-237's acceptance criteria
requires `FailureResponseFactory.Create<TResponse>(Error)`'s external signature and short-circuit behavior in
`AuthorizationBehavior`/`IdempotentCommandBehavior` to stay unchanged — only the internal dispatch mechanism changes.

Same WO-039 batch also contains P-238/P-239/P-240/P-241/P-242/P-243 in `05.Application`/`00.Governance` — unrelated
to this contract, not this domain's concern. See root `state-map.md` P-236 through P-244 for the full WO-039 set.
