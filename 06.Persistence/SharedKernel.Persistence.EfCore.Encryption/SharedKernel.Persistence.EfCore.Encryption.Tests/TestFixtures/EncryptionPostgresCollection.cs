using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;

[CollectionDefinition("EncryptionPostgres")]
public sealed class EncryptionPostgresCollection : ICollectionFixture<PostgreSqlContainerFixture>;
