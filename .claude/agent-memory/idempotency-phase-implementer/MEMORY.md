# Memory Index

- [Project: P-544 stateless-store follow-up](project_p544_stateless_store_followup.md) — (pre-WO-086 history) 2026-09-15: CompleteAsync/ReleaseAsync take an explicit reservation token and return bool; providers' ConcurrentDictionary removed
- [Project: P-544 IRequestIdempotencyStore migration](project_p544_request_idempotency_migration.md) — (pre-WO-086 history) single-interface rewrite, reservation-token guard added; contract now IIdempotencyStore
- [Project: 18.Idempotency first implementation](project_18idempotency_first_implementation.md) — (pre-WO-086 history) P-454/P-455 session, what shipped and what was open then
- [Redis atomicity protocol — exact commands](reference_redis_reservation_protocol.md) — RedisIdempotencyStore: hash-based TryBegin/Complete/Release Lua scripts, reservation token, purpose-kinded keys
- [EfCore atomicity protocol — exact SQL](reference_efcore_reservation_protocol.md) — EfCoreIdempotencyStore: INSERT...ON CONFLICT...RETURNING with per-CASE reclaim, token classification, raw ADO.NET
- [Npgsql/EF Core connectivity-exception wrapping gotcha](feedback_npgsql_exception_wrapping.md) — classifier must walk InnerException chain
- [Options namespace shadowing gotcha](feedback_options_namespace_shadowing.md) — `using {Package}.Options;` breaks `Microsoft.Extensions.Options.Options.Create`
- [Store registration lifetime — Scoped not Singleton](feedback_scoped_not_singleton_stores.md) — AddIdempotencyStore<T> default Scoped; check DbContext/scoped deps before accepting "singleton"
- [Testcontainers fail-open tests don't need Docker](reference_fail_open_tests_no_docker.md) — how to induce store-unavailable without a container
- [Redis test must not borrow a sibling test's short TTL](feedback_redis_test_ttl_must_not_borrow_shared_default.md) — real CI failure, root cause; each test passes its own `ttl` unless it tests expiry
