# Memory Index

- [Project: 18.Idempotency first implementation](project_18idempotency_first_implementation.md) — P-454/P-455 session, what shipped, what's still open
- [Redis atomicity protocol — exact commands](reference_redis_reservation_protocol.md) — SET NX PX, PEXPIRE, Lua script, sentinel value
- [EfCore atomicity protocol — exact SQL](reference_efcore_reservation_protocol.md) — the ON CONFLICT DO UPDATE upsert, response=NULL correction
- [Npgsql/EF Core connectivity-exception wrapping gotcha](feedback_npgsql_exception_wrapping.md) — classifier must walk InnerException chain
- [Options namespace shadowing gotcha](feedback_options_namespace_shadowing.md) — `using {Package}.Options;` breaks `Microsoft.Extensions.Options.Options.Create`
- [Store registration lifetime — Scoped not Singleton](feedback_scoped_not_singleton_stores.md) — corrected the work order's literal framing
- [Testcontainers fail-open tests don't need Docker](reference_fail_open_tests_no_docker.md) — how to induce store-unavailable without a container
- [Redis test must not borrow a sibling test's short InFlightTtl default](feedback_redis_test_ttl_must_not_borrow_shared_default.md) — real CI failure, root cause, and the EfCore asymmetry it doesn't share
