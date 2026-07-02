---
name: project-phase-p230
description: Locked design for P-230 (WO-038): IHasSuccessFlag and IResultOfT<T> application seams in SharedKernel.Primitives
metadata:
  type: project
---

P-230 adds two AOT-clean interfaces to `SharedKernel.Primitives` to unblock `05.Application.Behaviors` from reflection-based patterns.

**Why:** `LoggingBehavior` cannot distinguish failure from success on an unknown `TResponse` without runtime type inspection (which requires `[RequiresUnreferencedCode]`). `FailureResponseFactory` compiles `Expression<Func<Error, TResponse>>` at warm-up time — also `[RequiresUnreferencedCode]`. Both hazards are resolved by primitive interfaces that carry no AOT annotation.

**How to apply:** If `05.Application` asks for reflection-free Result outcome inspection or FailureResponseFactory-style construction, point to these two interfaces. They are the canonical seam — no workaround needed.

## Locked contract

`IHasSuccessFlag` (zero-member marker interface)
- Implemented by: `Result<T>` (sealed class) AND `Result` (non-generic readonly struct)
- Purpose: `response is IHasSuccessFlag` pattern match in `LoggingBehavior` — static IL `isinst`, AOT-safe
- Rule: must NEVER grow members; if behavioral addition is needed, create a new interface
- No `[RequiresUnreferencedCode]` — hard constraint

`IResultOfT<T>` (typed interface)
- Implemented by: `Result<T>` ONLY — `Result` (non-generic) must NOT implement it (no typed value payload)
- Exposes: `IsSuccess → bool`, `IsFailure → bool`, `Value → T`
- Does NOT expose `.Error` — keeps the interface minimal; callers needing `.Error` use the concrete type
- Purpose: enables `where TResponse : IResultOfT<TResponse>` generic constraint as a reflection-free substitute for `Expression`-compiled `FailureResponseFactory` in pipeline behaviors
- No `[RequiresUnreferencedCode]` — hard constraint

## Task IDs (SK.01.P230)
- D-26: IHasSuccessFlag design
- D-27: IResultOfT<T> design
- C-39: IHasSuccessFlag implementation (apply to Result<T> + Result)
- C-40: IResultOfT<T> implementation (apply to Result<T> only)
- T-29: IHasSuccessFlag tests (assignability + flag correctness)
- T-30: IResultOfT<T> tests (assignability, Value/IsSuccess/IsFailure surface, Value-on-failure throws, Result non-generic NOT assignable)
- DO-12: XML doc IHasSuccessFlag
- DO-13: XML doc IResultOfT<T>

## Cross-domain unlock
`05.Application.Behaviors` (WO-038) depends on P-230 shipping before it can eliminate its two reflection-based patterns. No other domain is affected.
