using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.ElasticSearch.Tests.Containers;

/// <summary>
/// xUnit collection definition binding every real-backend ElasticSearch provider test to a single
/// shared <see cref="ElasticsearchContainerFixture"/> instance — started once per test collection,
/// never per test method, per <c>16.Testing</c>'s own container-fixture rule. xUnit serializes test
/// classes within one collection, so each class provisioning/tearing down its own uniquely-named
/// index against the shared container is safe without further coordination.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ElasticsearchCollection : ICollectionFixture<ElasticsearchContainerFixture>
{
    /// <summary>The collection name test classes reference via <c>[Collection(ElasticsearchCollection.Name)]</c>.</summary>
    public const string Name = "ElasticSearch collection";
}
