# SharedKernel.Testing.Internal

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Not packable](https://img.shields.io/badge/packable-no-lightgrey)
![xUnit](https://img.shields.io/badge/test%20framework-xUnit-informational)

**The heavy test infrastructure this repository's own test suites share: Testcontainers fixtures, EF Core and
Npgsql helpers, and the MassTransit test harness.** It is `IsPackable=false` and never published — consuming
services use the packable `SharedKernel.*.Testing` packages instead (for PostgreSQL with the production roles,
`SharedKernel.Persistence.Testing`'s `PostgresTestServer`).

It is kept out of the published packages because it drags in Docker-only and framework-specific dependencies
(Testcontainers for seven engines, `xunit.core` for `IAsyncLifetime`, `MassTransit.TestFramework`,
`AWSSDK.S3`, EF Core SQLite) that a consumer's unit-test project should never pay for.

## Contents

| Namespace | Types | Notes |
| --- | --- | --- |
| `SharedKernel.Testing.Containers` | `PostgreSqlContainerFixture` (wraps `PostgresTestServer`), `RedisContainerFixture`, `RabbitMqContainerFixture`, `MinioContainerFixture`, `ElasticsearchContainerFixture` (`elasticsearch:9.4.2`), `MeilisearchContainerFixture` (`getmeili/meilisearch:v1.20.0`, hand-rolled — there is no Testcontainers module), `QdrantContainerFixture` (`qdrant/qdrant:v1.16.0` — collection metadata needs 1.16+) | xUnit `IAsyncLifetime`; share one per collection with `[CollectionDefinition]` + `ICollectionFixture<T>` |
| `SharedKernel.Testing.Persistence` | `TestSharedKernelDbContext`, `EfContextExtensions` (`DetachAll`, `ReloadAsync`, `RegisterOptions`), `PersistenceTestHelpers` (`AssertEntityTracked`/`NotTracked`), `TestNpgsqlConfiguration`, `TestNpgsqlDataSources`, `FakeAuditTrailWriter`, `FakeAuditQueryService`, `FakeAuditActorContext` | EF Core, Npgsql and audit-ledger helpers for the persistence suites |
| `SharedKernel.Testing.Messaging` | `TestHarnessFactory.CreateAsync(...)` | A started MassTransit `ITestHarness` for consumer and filter tests |

## Use

Only from test projects inside this repository, as a `ProjectReference`:

```xml
<ProjectReference Include="..\..\..\16.Testing\SharedKernel.Testing.Internal\SharedKernel.Testing.Internal.csproj" />
```

```csharp
[CollectionDefinition(nameof(RedisCollection))]
public sealed class RedisCollection : ICollectionFixture<RedisContainerFixture>;

[Collection(nameof(RedisCollection))]
public sealed class RedisLockTests(RedisContainerFixture redis)
{
    // redis.ConnectionString points at a real, per-collection Redis container
}
```

Never hand-roll a competing container setup inside a `.Tests` project; add the fixture here. Its own tests
(`SharedKernel.Testing.Internal.Tests`) run in the Integration lane (`Platform.SharedKernel.Integration.slnf`) and
need Docker.

## Related packages

- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) and
  [`SharedKernel.Persistence.Testing`](../SharedKernel.Persistence.Testing/README.md) — the packable packages it
  builds on.
