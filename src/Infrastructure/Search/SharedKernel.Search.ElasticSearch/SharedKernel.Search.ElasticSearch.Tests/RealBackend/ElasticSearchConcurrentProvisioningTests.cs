using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Tests.Containers;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.ElasticSearch.Tests.RealBackend;

/// <summary>
/// <c>EnsureIndexAsync</c> run by several replicas of one service at the same time, the way a deployment starts them.
/// Every replica must succeed: the ones that lose the create race find the index the winner created.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
public sealed class ElasticSearchConcurrentProvisioningTests(ElasticsearchContainerFixture fixture)
{
    private const int Replicas = 6;

    [Fact]
    public async Task ReplicasProvisioningTheSameNewIndexConcurrently_AllSucceed_AndTheIndexCarriesTheFingerprint()
    {
        var definition = TestProductIndexDefinitions.WithTextAnalysis(
            $"concurrent-new-{Guid.NewGuid():N}",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["rodent"] = ["mouse"] },
            ["the", "a", "with"]);
        var client = ElasticsearchProviderFactory.CreateClient(fixture);
        try
        {
            // One provisioner per replica, as each process has its own.
            var outcomes = await Task.WhenAll(
                Enumerable.Range(0, Replicas).Select(_ =>
                    ElasticsearchProviderFactory.CreateProvisioner(client).EnsureIndexAsync(definition)));

            outcomes.Should().AllSatisfy(outcome =>
                outcome.IsSuccess.Should().BeTrue(outcome.IsFailure ? outcome.Error.Message : "every replica succeeds"));

            var registered = new Dictionary<string, SearchIndexDefinition>(StringComparer.Ordinal)
            {
                [definition.Name] = definition,
            };
            var verified = await ElasticsearchProviderFactory
                .CreateProvisioner(client, registeredDefinitions: registered)
                .VerifyRegisteredIndexesAsync();
            verified.IsSuccess.Should().BeTrue(verified.IsFailure ? verified.Error.Message : "the fingerprint matches");
        }
        finally
        {
            await ElasticsearchProviderFactory.CreateProvisioner(client).DeleteIndexAsync(definition.Name);
        }
    }
}
