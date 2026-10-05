# SharedKernel.Testing.Internal

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Not packable](https://img.shields.io/badge/packable-no-lightgrey)

> **The heavy test infrastructure this repository's own suites share: Testcontainers fixtures, EF Core and Npgsql
> helpers, and a MassTransit test harness. It is never published.**

Consuming services use the packable `SharedKernel.*.Testing` packages instead — for a PostgreSQL with the
production role split, [`SharedKernel.Persistence.Testing`](../../Infrastructure/Persistence/SharedKernel.Persistence.Testing/README.md)'s
`PostgresTestServer`. This project stays out of the release because it drags in Docker-only and framework-specific
dependencies (Testcontainers for seven engines, `xunit.core` for `IAsyncLifetime`, `MassTransit.TestFramework`,
`AWSSDK.S3`, EF Core SQLite) that a consumer's unit-test project should never pay for.

## What is inside

| Namespace | Types |
| --- | --- |
| `SharedKernel.Testing.Containers` | `PostgreSqlContainerFixture` (wraps `PostgresTestServer`), `RedisContainerFixture` (`redis:7.4`), `RabbitMqContainerFixture` (`rabbitmq:3.13-management`), `MinioContainerFixture`, `ElasticsearchContainerFixture` (`elasticsearch:9.4.2`), `MeilisearchContainerFixture` (`getmeili/meilisearch:v1.20.0`, a generic container — there is no Testcontainers module), `QdrantContainerFixture` (`qdrant/qdrant:v1.16.0` — collection metadata needs 1.16+) |
| `SharedKernel.Testing.Persistence` | `TestSharedKernelDbContext`, `EfContextExtensions` (`DetachAll`, `ReloadAsync`, `RegisterOptions`), `PersistenceTestHelpers` (`AssertEntityTracked`/`AssertEntityNotTracked`), `TestNpgsqlConfiguration`, `TestNpgsqlDataSources`, `FakeAuditTrailWriter`, `FakeAuditQueryService`, `FakeAuditActorContext` |
| `SharedKernel.Testing.Messaging` | `TestHarnessFactory.CreateAsync(serviceName, configure)` — a started MassTransit `ITestHarness` with the kebab-case endpoint formatter |

Every container fixture is an xUnit `IAsyncLifetime` with pinned image tags, so CI is reproducible.

## Use

Only from test projects inside this repository, as a `ProjectReference`:

```xml
<ProjectReference Include=".\SharedKernel.Testing.Internal.csproj" />
```

Share one container per collection:

```csharp
using SharedKernel.Testing.Containers;
using Xunit;

[CollectionDefinition(nameof(RedisCollection))]
public sealed class RedisCollection : ICollectionFixture<RedisContainerFixture>;

[Collection(nameof(RedisCollection))]
public sealed class RedisLockTests(RedisContainerFixture redis)
{
    // redis.ConnectionString points at a real Redis container shared by the collection
}
```

## Rules

- Never hand-roll a competing container setup inside a `.Tests` project; add the fixture here.
- Never reference this project from a packable package or from production code.
- Its own tests (`SharedKernel.Testing.Internal.Tests`) run in the Integration lane
  (`Platform.SharedKernel.Integration.slnf`) and need Docker.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[16.Testing domain](../README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
