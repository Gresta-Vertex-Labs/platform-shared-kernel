---
name: generic_result_failure_construction
description: How AuthorizationBehavior/IdempotentCommandBehavior build a Result/Result<T> failure from Error when TResponse is generic, without reflection
metadata:
  type: project
---

`SharedKernel.Application.Behaviors` needed a way to short-circuit a MediatR pipeline behavior
with a failed response of type `TResponse`, where `TResponse` is only known to satisfy
`IRequest<TResponse>` — at runtime it's either the non-generic `Result` (struct) or a closed
`Result<T>` (sealed class), with no shared interface linking them.

**CURRENT (revised) solution, as of 2026-06-29 — supersedes the original `dynamic` approach:**
`Shared/FailureResponseFactory.cs` (`internal static class FailureResponseFactory`,
`TResponse Create<TResponse>(Error error)`). Special-cases `typeof(TResponse) == typeof(Result)`
directly. For the `Result<T>` case, builds a small `Expression` tree per distinct closed `TResponse`
type that performs the implicit `Error -> Result<T>` conversion (`Expression.Convert` through the
`public static implicit operator`), compiles it once via `Expression.Lambda<Func<Error,object>>(...)
.Compile()`, and caches it in a static `ConcurrentDictionary<Type, Func<Error, object>>` keyed by
`TResponse`. This is the SECOND documented, justified exception to the platform-wide
`MakeGenericMethod`/reflection prohibition in `05.Application` (the first being
`MediatRDomainEventDispatcher`'s per-event-type dispatch cache) — built via `Expression` compilation
(the governance rule's own recommended alternative to reflection), not `MakeGenericMethod`.

**Why the original `dynamic`/`Microsoft.CSharp` approach was rejected:** it pulled in a new
`Microsoft.CSharp` NuGet package reference this domain had no other reason to carry, and is not
AOT/trim-safe (DLR-based). Replaced before the Tests phase began. `Microsoft.CSharp` package
reference has been REMOVED from `SharedKernel.Application.Behaviors.csproj` — do not re-add it.

**How to apply:** If a future behavior needs the same "construct Result/Result&lt;T&gt; from Error
generically" capability, reuse `FailureResponseFactory.Create<TResponse>(error)` rather than
re-deriving a new mechanism. Never use `dynamic` for this class of problem in this domain — the
cached-compiled-`Expression` pattern is the chosen, documented answer.

See also [[seven_step_pipeline_implementation]].
