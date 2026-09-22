using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;

[CollectionDefinition("EncryptionPostgres")]
public sealed class EncryptionPostgresCollection : ICollectionFixture<PostgreSqlContainerFixture>;
