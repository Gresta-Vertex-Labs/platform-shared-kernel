using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Tests.Containers;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.Meilisearch.Tests.RealBackend;

/// <summary>
/// <c>EnsureIndexAsync</c> run by several replicas of one service at the same time, the way a deployment starts them.
/// Every replica must succeed and the index must end up configured once, never half-configured or "conflicting".
/// </summary>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchConcurrentProvisioningTests(MeilisearchContainerFixture fixture)
{
    private const int Replicas = 6;

    [Fact]
    public async Task ReplicasProvisioningTheSameNewIndexConcurrently_AllSucceed_AndTheIndexIsConfigured()
    {
        var definition = Definition($"concurrent-new-{Guid.NewGuid():N}");
        try
        {
            // One provisioner per replica, as each process has its own.
            var outcomes = await Task.WhenAll(
                Enumerable.Range(0, Replicas).Select(_ =>
                    MeilisearchProviderFactory.CreateProvisioner(fixture).EnsureIndexAsync(definition)));

            outcomes.Should().AllSatisfy(outcome =>
                outcome.IsSuccess.Should().BeTrue(outcome.IsFailure ? outcome.Error.Message : "every replica succeeds"));
            await AssertConfiguredAsync(definition);
        }
        finally
        {
            await MeilisearchProviderFactory.CreateProvisioner(fixture).DeleteIndexAsync(definition.Name);
        }
    }

    [Fact]
    public async Task IndexCreatedButNotYetConfiguredByAnotherReplica_IsConfigured_NotReportedAsATextAnalysisConflict()
    {
        // The window between a replica's create task and its settings task: the index exists, with no synonyms,
        // no stop words and no fingerprint. A second replica arriving now must configure it, not refuse it.
        var definition = Definition($"concurrent-unclaimed-{Guid.NewGuid():N}");
        var client = MeilisearchProviderFactory.CreateClient(fixture);
        var created = await client.CreateIndexAsync(definition.Name, definition.PrimaryKeyField);
        await client.WaitForTaskAsync(created.TaskUid);
        try
        {
            var outcome = await MeilisearchProviderFactory.CreateProvisioner(fixture).EnsureIndexAsync(definition);

            outcome.IsSuccess.Should().BeTrue(outcome.IsFailure ? outcome.Error.Message : "the index is unclaimed and empty");
            await AssertConfiguredAsync(definition);
        }
        finally
        {
            await MeilisearchProviderFactory.CreateProvisioner(fixture).DeleteIndexAsync(definition.Name);
        }
    }

    [Fact]
    public async Task PopulatedIndexWithoutAFingerprint_StillRefusesADifferentTextAnalysis()
    {
        // The relaxation above is for empty, unclaimed indexes only. Documents indexed under other text analysis
        // keep the index held to it, exactly as before.
        var definition = Definition($"concurrent-populated-{Guid.NewGuid():N}");
        var client = MeilisearchProviderFactory.CreateClient(fixture);
        var created = await client.CreateIndexAsync(definition.Name, definition.PrimaryKeyField);
        await client.WaitForTaskAsync(created.TaskUid);
        var added = await client.Index(definition.Name).AddDocumentsAsync(TestProductCorpus.All.Take(1));
        await client.WaitForTaskAsync(added.TaskUid);
        try
        {
            var outcome = await MeilisearchProviderFactory.CreateProvisioner(fixture).EnsureIndexAsync(definition);

            outcome.IsFailure.Should().BeTrue();
            outcome.Error.Code.Should().Be("search.index_definition_conflict");
        }
        finally
        {
            await MeilisearchProviderFactory.CreateProvisioner(fixture).DeleteIndexAsync(definition.Name);
        }
    }

    private static SearchIndexDefinition Definition(string name) =>
        new SearchIndexDefinitionBuilder(name)
            .ConfigureSharedFields()
            .Synonym("rodent", "mouse")
            .StopWords("the", "a", "with")
            .Build()
            .Value;

    private async Task AssertConfiguredAsync(SearchIndexDefinition definition)
    {
        // The index carries the definition's fingerprint, so it was configured, not merely created.
        var registered = new Dictionary<string, SearchIndexDefinition> { [definition.Name] = definition };
        var verified = await MeilisearchProviderFactory
            .CreateProvisioner(fixture, registeredDefinitions: registered)
            .VerifyRegisteredIndexesAsync();
        verified.IsSuccess.Should().BeTrue(verified.IsFailure ? verified.Error.Message : "the fingerprint matches");

        // The text analysis reached the engine: the declared synonym makes "rodent" find the mouse.
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(fixture, definition);
        (await index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();
        var found = await index.SearchAsync(
            SearchRequest.Default with { FreeText = "rodent", PageSize = 20 },
            TenantScope.For(TestProductCorpus.TenantA));
        found.IsSuccess.Should().BeTrue();
        found.Value.Hits.Should().Contain(hit => hit.Document.Name == "Wireless Mouse");
    }
}
