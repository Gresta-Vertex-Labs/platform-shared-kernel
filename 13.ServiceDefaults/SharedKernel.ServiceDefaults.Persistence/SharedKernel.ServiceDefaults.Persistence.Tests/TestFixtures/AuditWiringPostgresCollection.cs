using SharedKernel.Testing.Containers;

namespace SharedKernel.ServiceDefaults.Persistence.Tests.TestFixtures;

[CollectionDefinition("AuditWiringPostgres")]
public sealed class AuditWiringPostgresCollection : ICollectionFixture<PostgreSqlContainerFixture>;
