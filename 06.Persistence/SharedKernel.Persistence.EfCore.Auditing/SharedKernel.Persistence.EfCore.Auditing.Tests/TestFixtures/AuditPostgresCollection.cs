using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.TestFixtures;

[CollectionDefinition("AuditPostgres")]
public sealed class AuditPostgresCollection : ICollectionFixture<PostgreSqlContainerFixture>;
