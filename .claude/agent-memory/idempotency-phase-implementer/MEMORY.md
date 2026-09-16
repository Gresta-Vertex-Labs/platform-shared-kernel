# Memory Index

- [Project: P-544 stateless-store follow-up](project_p544_stateless_store_followup.md) — same-day 2026-09-15 refinement: CompleteAsync/ReleaseAsync take an explicit reservationToken and return bool; both providers' ConcurrentDictionary removed; ApplicationBehaviorsBuilder.Build() now calls AddLogging()
- [Project: P-544 IRequestIdempotencyStore migration](project_p544_request_idempotency_migration.md) — 2026-09-15 pre-publish rewrite onto the new single-interface contract, reservation-token guard added (superseded same-day by the stateless follow-up above)
- [Project: 18.Idempotency first implementation](project_18idempotency_first_implementation.md) — P-454/P-455 session, what shipped, what's still open (superseded in part by P-544, still useful for scaffold/layering history)
- [Redis atomicity protocol — exact commands](reference_redis_reservation_protocol.md) — hash-based TryBegin/Complete/Release Lua scripts, reservation token, message-store sentinel unchanged
- [EfCore atomicity protocol — exact SQL](reference_efcore_reservation_protocol.md) — INSERT...ON CONFLICT...RETURNING with per-CASE reclaim, reservation-token classification, raw ADO.NET via context connection
- [Npgsql/EF Core connectivity-exception wrapping gotcha](feedback_npgsql_exception_wrapping.md) — classifier must walk InnerException chain
- [Options namespace shadowing gotcha](feedback_options_namespace_shadowing.md) — `using {Package}.Options;` breaks `Microsoft.Extensions.Options.Options.Create`
- [Store registration lifetime — Scoped not Singleton](feedback_scoped_not_singleton_stores.md) — corrected the work order's literal framing
- [Testcontainers fail-open tests don't need Docker](reference_fail_open_tests_no_docker.md) — how to induce store-unavailable without a container
- [Redis test must not borrow a sibling test's short InFlightTtl default](feedback_redis_test_ttl_must_not_borrow_shared_default.md) — real CI failure, root cause, and the EfCore asymmetry it doesn't share
