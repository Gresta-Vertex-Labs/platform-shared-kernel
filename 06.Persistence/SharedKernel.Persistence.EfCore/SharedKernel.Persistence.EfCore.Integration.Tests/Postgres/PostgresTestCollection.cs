using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

/// <summary>
/// Shares a single <see cref="PostgreSqlContainerFixture"/> across every
/// <c>[Collection("EfCorePostgres")]</c>-tagged class in this assembly (coordinator
/// follow-up). xUnit collection definitions are assembly-local, so this mirrors
/// <c>SharedKernel.Persistence.EfCore.Tests</c>'s own <c>PostgreSqlTestCollection</c> rather than
/// reusing it directly. Every test targets its own uniquely-named database on the shared container
/// (see each test class's own <c>DatabaseName</c> constant), matching that project's established
/// pattern for parallel-safe isolation without paying for a fresh container per test class.
/// </summary>
[CollectionDefinition("EfCorePostgres")]
public sealed class PostgresTestCollection : ICollectionFixture<PostgreSqlContainerFixture>
{
}
