using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Integration;

/// <summary>
/// Shares a single <see cref="PostgreSqlContainerFixture"/> instance across every
/// <c>[Collection("PostgreSQL")]</c>-tagged integration test class in this assembly.
/// </summary>
/// <remarks>
/// Completes the sharing the pre-existing <c>[Collection("PostgreSQL")]</c> tags on
/// <see cref="ConcurrencyIntegrationTests"/>, <see cref="KeysetPaginationIntegrationTests"/>, and
/// <see cref="TransientFaultRetryIntegrationTests"/> always implied but never had a matching
/// <c>[CollectionDefinition]</c> to back — those classes previously each started and disposed their
/// own independent <c>PostgreSqlContainer</c> despite already being serialized against one another.
/// <see cref="PostgreSQLIntegrationTests"/> deliberately does NOT join this collection — its pgvector
/// round-trip test requires the <c>pgvector/pgvector:pg16</c> image, which this shared fixture's
/// plain <c>postgres:16.4</c> image does not provide (the pgvector extension binary/shared library is
/// simply absent from a vanilla PostgreSQL image; <c>CREATE EXTENSION vector</c> cannot succeed
/// against it). That class continues to manage its own dedicated, pgvector-enabled container.
/// </remarks>
[CollectionDefinition("PostgreSQL")]
public sealed class PostgreSqlTestCollection : ICollectionFixture<PostgreSqlContainerFixture>
{
}
