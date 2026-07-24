using SharedKernel.Testing.Containers;

namespace SharedKernel.AI.Qdrant.Tests.Conformance;

/// <summary>
/// The xUnit test collection sharing one <see cref="QdrantContainerFixture"/> instance across every
/// real-backend conformance test class in this file group — started once, never per test method.
/// </summary>
[CollectionDefinition(Name)]
public sealed class QdrantConformanceCollection : ICollectionFixture<QdrantContainerFixture>
{
    /// <summary>The collection name every conformance test class references via <c>[Collection(Name)]</c>.</summary>
    public const string Name = "Qdrant Conformance";
}
