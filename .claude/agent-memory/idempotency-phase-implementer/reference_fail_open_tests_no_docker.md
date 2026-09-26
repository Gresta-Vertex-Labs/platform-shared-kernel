---
name: reference_fail_open_tests_no_docker
description: How to write and run genuine fail-open/fail-closed store-unavailability tests without a Docker daemon or Testcontainers, for both Redis and PostgreSQL.
type: reference
---

Written for `18.Idempotency`'s T-05 (`.Redis`) and T-10 (`.EfCore`) tasks, both of which ran green in an environment with Docker completely unavailable.

**Redis**: point a real `IConnectionMultiplexer` at a loopback port nothing listens on, with `abortConnect=false` so `Connect()` itself succeeds (StackExchange.Redis's own `AbortOnConnectFail=false` behavior, same as `02.Caching.Redis.Core`'s `AddRedisConnection` sets by default) and the connectivity failure only surfaces when a command actually executes:
```csharp
ConnectionMultiplexer.Connect("127.0.0.1:1,abortConnect=false,connectTimeout=300,connectRetry=0,syncTimeout=300");
```
A subsequent command (the store's `ScriptEvaluateAsync`) throws a real `RedisConnectionException`/`RedisTimeoutException` within the configured timeout — genuinely exercises `RedisStoreUnavailableClassifier`.

**PostgreSQL**: same idea via Npgsql connection-string timeouts:
```
Host=127.0.0.1;Port=1;Database=unreachable;Username=sk;Password=sk;Timeout=1;Command Timeout=1
```
A query against a context built on this connection string throws within ~1 second. See [[feedback_npgsql_exception_wrapping]] for the exact (surprising) exception shape this produces and why the classifier must walk `InnerException`.

**Test file placement warning**: these tests must live in a STANDALONE xUnit test class with no `[Collection("...")]` attribute tying it to a Testcontainers fixture's `ICollectionFixture<T>`. If nested inside a class/collection that also contains real container-backed concurrency tests, xUnit will try to start the real container for the whole collection before running ANY test in it — including the fail-open ones that don't need it — making them fail to run at all without Docker. This was a real mistake made and fixed in this session (`EfCoreIdempotencyFailOpenTests` was extracted out of `EfCoreIdempotencyConcurrencyTests`'s `[Collection("PostgreSqlContainer")]` into its own file with no collection attribute).

**Net effect**: 40 of the ~50 tests across both `18.Idempotency` packages needed zero Docker and all ran green in a Docker-less sandbox — only the true multi-connection concurrency races (N parallel calls racing for one reservation) genuinely need a real shared backing store and can't be faked this way.
