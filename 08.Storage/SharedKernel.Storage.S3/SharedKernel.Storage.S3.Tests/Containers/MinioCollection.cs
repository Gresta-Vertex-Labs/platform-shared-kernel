using SharedKernel.Testing.Containers;

namespace SharedKernel.Storage.S3.Tests.Containers;

/// <summary>
/// xUnit collection definition binding every real-backend S3 provider test to a single shared
/// <see cref="MinioContainerFixture"/> instance — started once per test collection, never per test
/// method, per <c>16.Testing</c>'s own container-fixture rule.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MinioCollection : ICollectionFixture<MinioContainerFixture>
{
    /// <summary>The collection name test classes reference via <c>[Collection(MinioCollection.Name)]</c>.</summary>
    public const string Name = "S3 MinIO collection";
}
