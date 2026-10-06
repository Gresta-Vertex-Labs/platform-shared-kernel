using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Provisioning;
using SharedKernel.AI.Qdrant.Tests.TestSupport;
using SharedKernel.Testing.Containers;

namespace SharedKernel.AI.Qdrant.Tests.Conformance;

/// <summary>
/// <c>EnsureCollectionAsync</c> run by several replicas of one service at the same time, the way a deployment starts
/// them. Every replica must succeed: the ones that lose the create race find the collection the winner created.
/// </summary>
[Collection(QdrantConformanceCollection.Name)]
public sealed class QdrantConcurrentProvisioningTests(QdrantContainerFixture fixture)
{
    private const int Replicas = 6;

    [Fact]
    public async Task ReplicasProvisioningTheSameNewCollectionConcurrently_AllSucceed()
    {
        var name = $"concurrent-new-{Guid.NewGuid():N}";
        var definition = new VectorCollectionDefinitionBuilder(name)
            .EmbeddingModel("concurrent-model", 3)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Field("category", VectorFieldKind.String, filterable: true)
            .Build()
            .Value;
        var grpc = new Uri(fixture.GrpcEndpoint);

        // One client and provisioner per replica, as each process has its own.
        var replicas = Enumerable
            .Range(0, Replicas)
            .Select(_ => new QdrantCollectionProvisioner(
                new QdrantClient(grpc.Host, grpc.Port),
                NullLogger<QdrantCollectionProvisioner>.Instance))
            .ToArray();
        try
        {
            var outcomes = await Task.WhenAll(replicas.Select(replica => replica.EnsureCollectionAsync(definition)));

            outcomes.Should().AllSatisfy(outcome =>
                outcome.IsSuccess.Should().BeTrue(outcome.IsFailure ? outcome.Error.Message : "every replica succeeds"));
            var again = await replicas[0].EnsureCollectionAsync(definition);
            again.IsSuccess.Should().BeTrue("the collection carries the definition's fingerprint");
        }
        finally
        {
            await replicas[0].DeleteCollectionAsync(name);
        }
    }
}
