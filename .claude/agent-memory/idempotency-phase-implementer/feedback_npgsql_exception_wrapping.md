---
name: feedback_npgsql_exception_wrapping
description: EF Core's NpgsqlExecutionStrategy wraps real connectivity exceptions in InvalidOperationException even without EnableRetryOnFailure configured — a narrow exception classifier must walk the InnerException chain, not just check the top-level type.
type: feedback
---

Discovered while testing `18.Idempotency/SharedKernel.Idempotency.EfCore`'s `EfCoreStoreUnavailableClassifier` against a genuinely unreachable Postgres endpoint (`127.0.0.1:1`, connection refused).

**What happened:** the exception that actually surfaces at the store's `catch (Exception ex) when (...)` filter is `System.InvalidOperationException` with message "An exception has been raised that is likely due to a transient failure." — NOT the real `Npgsql.NpgsqlException`/`System.TimeoutException`. The real exception is nested: `InvalidOperationException` → `NpgsqlException` → `TimeoutException`. This happens via `NpgsqlExecutionStrategy.ExecuteAsync` even when `EnableRetryOnFailure` was never called — EF Core's default execution strategy machinery still wraps this way for `ExecuteSqlInterpolatedAsync`/`ExecuteUpdateAsync` calls.

**Why: a classifier that only pattern-matches the top-level exception type will silently fail to recognize a real outage** — the `when` filter evaluates false, the exception propagates unclassified, and `AllowExecutionOnStoreUnavailable = true` never actually kicks in. This was caught by an executable test (`EfCoreIdempotencyFailOpenTests`, no Docker needed — see [[reference_fail_open_tests_no_docker]]), not by code review.

**How to apply:** a "genuine connectivity/timeout" exception classifier for any EF Core + Npgsql code path in this repo must walk `Exception.InnerException` (bounded depth, e.g. 5 hops, to stay a "narrow classifier" and not degrade into `catch (Exception)`), not just switch on the exception passed to the catch clause. See `EfCoreStoreUnavailableClassifier.IsStoreUnavailable` for the working implementation — it loops through the InnerException chain checking `NpgsqlException{IsTransient:true}`, `NpgsqlException{InnerException: SocketException or TimeoutException}`, bare `TimeoutException`, and bare `SocketException` at each level. If `06.Persistence` or any other domain ever writes a similar Npgsql-connectivity classifier, check this pattern first rather than re-deriving it from scratch.
