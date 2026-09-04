using SharedKernel.Testing.Containers;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Concurrency;

/// <summary>
/// Shares one <see cref="PostgreSqlContainerFixture"/> across every test class in the
/// <c>PostgreSqlContainer</c> collection — the container is started once and reused, not once per
/// test.
/// </summary>
/// <remarks>
/// REQUIRES A DOCKER DAEMON. The fixture's <c>InitializeAsync</c> starts a real Testcontainers
/// PostgreSQL container; every test in this collection fails at collection setup, not at the
/// individual test method, when no Docker daemon is reachable.
/// </remarks>
[CollectionDefinition("PostgreSqlContainer")]
public sealed class PostgreSqlContainerCollection : ICollectionFixture<PostgreSqlContainerFixture>;
