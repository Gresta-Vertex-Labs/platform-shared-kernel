using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Testing.Search;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Search;

/// <summary>
/// Proves <see cref="InMemorySearchIndexProvisioner"/> against
/// <c>ISearchIndexProvisioner</c>'s documented idempotent/additive-only/cutover contract — no
/// consuming domain has adopted this fake yet, so this self-test is the only behavioral proof
/// today, per the SelfTests routing rule.
/// </summary>
public sealed class InMemorySearchIndexProvisionerTests
{
    [Fact]
    public async Task EnsureIndexAsync_NewDefinition_Registers()
    {
        var provisioner = new InMemorySearchIndexProvisioner();

        var result = await provisioner.EnsureIndexAsync(BuildDefinition("products", ["Name"]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("products", provisioner.RegisteredIndexNames);
    }

    [Fact]
    public async Task EnsureIndexAsync_IdenticalRedeclaration_IsIdempotent()
    {
        var provisioner = new InMemorySearchIndexProvisioner();
        var definition = BuildDefinition("products", ["Name"]);
        await provisioner.EnsureIndexAsync(definition, CancellationToken.None);

        var result = await provisioner.EnsureIndexAsync(definition, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EnsureIndexAsync_AdditiveNewField_MergesWithoutDroppingExisting()
    {
        var provisioner = new InMemorySearchIndexProvisioner();
        await provisioner.EnsureIndexAsync(BuildDefinition("products", ["Name"]), CancellationToken.None);

        var withExtraField = BuildDefinition("products", ["Name", "Status"]);
        var result = await provisioner.EnsureIndexAsync(withExtraField, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Both fields are now part of the stored definition: re-declaring either with a different role conflicts.
        foreach (var field in new[] { "Name", "Status" })
        {
            var conflicting = SearchIndexDefinition.Create(
                "products",
                [new SearchFieldDefinition { Name = field, Kind = SearchFieldKind.Text, Searchable = false, Filterable = true }]).Value;
            var conflict = await provisioner.EnsureIndexAsync(conflicting, CancellationToken.None);
            Assert.Equal(SearchErrors.IndexDefinitionConflict("products", field), conflict.Error);
        }
    }

    [Fact]
    public async Task EnsureIndexAsync_ConflictingRoleForSameField_ReturnsIndexDefinitionConflict()
    {
        var provisioner = new InMemorySearchIndexProvisioner();
        await provisioner.EnsureIndexAsync(
            SearchIndexDefinition.Create(
                "products",
                [new SearchFieldDefinition { Name = "Name", Kind = SearchFieldKind.Text, Searchable = true }]).Value,
            CancellationToken.None);

        var conflicting = SearchIndexDefinition.Create(
            "products",
            [new SearchFieldDefinition { Name = "Name", Kind = SearchFieldKind.Text, Searchable = false, Filterable = true }]).Value;

        var result = await provisioner.EnsureIndexAsync(conflicting, CancellationToken.None);

        Assert.Equal(SearchErrors.IndexDefinitionConflict("products", "Name"), result.Error);
    }

    [Fact]
    public async Task EnsureIndexAsync_SimulateFailure_ReturnsFailure_AndDoesNotRegister()
    {
        var provisioner = new InMemorySearchIndexProvisioner { SimulateFailure = true };

        var result = await provisioner.EnsureIndexAsync(BuildDefinition("products", ["Name"]), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(provisioner.RegisteredIndexNames);
    }

    [Fact]
    public async Task IndexExistsAsync_RegisteredAndUnregistered()
    {
        var provisioner = new InMemorySearchIndexProvisioner();
        await provisioner.EnsureIndexAsync(BuildDefinition("products", ["Name"]), CancellationToken.None);

        Assert.True((await provisioner.IndexExistsAsync("products", CancellationToken.None)).Value);
        Assert.False((await provisioner.IndexExistsAsync("never-registered", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task DeleteIndexAsync_AbsentName_IsIdempotent()
    {
        var provisioner = new InMemorySearchIndexProvisioner();

        var result = await provisioner.DeleteIndexAsync("never-registered", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteIndexAsync_RegisteredName_Removes()
    {
        var provisioner = new InMemorySearchIndexProvisioner();
        await provisioner.EnsureIndexAsync(BuildDefinition("products", ["Name"]), CancellationToken.None);

        var result = await provisioner.DeleteIndexAsync("products", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False((await provisioner.IndexExistsAsync("products", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task DeleteIndexAsync_SimulateFailure_ReturnsFailure_AndLeavesIndexRegistered()
    {
        var provisioner = new InMemorySearchIndexProvisioner();
        await provisioner.EnsureIndexAsync(BuildDefinition("products", ["Name"]), CancellationToken.None);
        provisioner.SimulateFailure = true;

        var result = await provisioner.DeleteIndexAsync("products", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True((await provisioner.IndexExistsAsync("products", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task CutoverAsync_UnregisteredStaging_ReturnsCutoverFailed()
    {
        var provisioner = new InMemorySearchIndexProvisioner();

        var result = await provisioner.CutoverAsync(
            new IndexCutoverRequest { StagingIndexName = "products-staging", LiveIndexName = "products" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("search.cutover_failed", result.Error.Code);
    }

    [Fact]
    public async Task CutoverAsync_DeleteStagingAfterCutover_True_PromotesLive_AndRemovesStaging()
    {
        var provisioner = new InMemorySearchIndexProvisioner();
        await provisioner.EnsureIndexAsync(BuildDefinition("products-staging", ["Name"]), CancellationToken.None);

        var result = await provisioner.CutoverAsync(
            new IndexCutoverRequest { StagingIndexName = "products-staging", LiveIndexName = "products", DeleteStagingAfterCutover = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True((await provisioner.IndexExistsAsync("products", CancellationToken.None)).Value);
        Assert.False((await provisioner.IndexExistsAsync("products-staging", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task CutoverAsync_DeleteStagingAfterCutover_False_RetainsStaging()
    {
        var provisioner = new InMemorySearchIndexProvisioner();
        await provisioner.EnsureIndexAsync(BuildDefinition("products-staging", ["Name"]), CancellationToken.None);

        var result = await provisioner.CutoverAsync(
            new IndexCutoverRequest { StagingIndexName = "products-staging", LiveIndexName = "products", DeleteStagingAfterCutover = false },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True((await provisioner.IndexExistsAsync("products-staging", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task CutoverAsync_SimulateFailure_ReturnsFailure()
    {
        var provisioner = new InMemorySearchIndexProvisioner();
        await provisioner.EnsureIndexAsync(BuildDefinition("products-staging", ["Name"]), CancellationToken.None);
        provisioner.SimulateFailure = true;

        var result = await provisioner.CutoverAsync(
            new IndexCutoverRequest { StagingIndexName = "products-staging", LiveIndexName = "products" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Reset_ClearsAllRegistrations()
    {
        var provisioner = new InMemorySearchIndexProvisioner();
        await provisioner.EnsureIndexAsync(BuildDefinition("products", ["Name"]), CancellationToken.None);

        provisioner.Reset();

        Assert.Empty(provisioner.RegisteredIndexNames);
    }

    private static SearchIndexDefinition BuildDefinition(string name, IReadOnlyList<string> fieldNames)
    {
        var fields = fieldNames
            .Select(n => new SearchFieldDefinition { Name = n, Kind = SearchFieldKind.Text, Searchable = true })
            .ToArray();
        return SearchIndexDefinition.Create(name, fields).Value;
    }
}
