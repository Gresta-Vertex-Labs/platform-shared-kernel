using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.Meilisearch.Tests.Containers;

/// <summary>
/// xUnit collection definition binding every real-backend Meilisearch provider test to a single shared
/// <see cref="MeilisearchContainerFixture"/> instance — started once per test collection, never per test
/// method, per <c>16.Testing</c>'s own container-fixture rule.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MeilisearchCollection : ICollectionFixture<MeilisearchContainerFixture>
{
    /// <summary>The collection name test classes reference via <c>[Collection(MeilisearchCollection.Name)]</c>.</summary>
    public const string Name = "Meilisearch collection";
}
