using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Testing.Intelligence;

namespace SharedKernel.Testing.SelfTests.Intelligence;

/// <summary>
/// Proves <see cref="InMemoryVectorCollectionProvisioner"/> against
/// <c>IVectorCollectionProvisioner</c>'s documented idempotent/additive-only/cutover contract -- no
/// consuming domain has adopted this fake yet, so this self-test is the only behavioral proof today,
/// per the SelfTests routing rule.
/// </summary>
public sealed class InMemoryVectorCollectionProvisionerTests
{
    [Fact]
    public async Task EnsureCollectionAsync_NewDefinition_Registers()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();

        var result = await provisioner.EnsureCollectionAsync(BuildDefinition("chunks", ["Status"]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("chunks", provisioner.RegisteredCollectionNames);
    }

    [Fact]
    public async Task EnsureCollectionAsync_IdenticalRedeclaration_IsIdempotent()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();
        var definition = BuildDefinition("chunks", ["Status"]);
        await provisioner.EnsureCollectionAsync(definition, CancellationToken.None);

        var result = await provisioner.EnsureCollectionAsync(definition, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EnsureCollectionAsync_AdditiveNewField_MergesWithoutDroppingExisting()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();
        await provisioner.EnsureCollectionAsync(BuildDefinition("chunks", ["Status"]), CancellationToken.None);

        var withExtraField = BuildDefinition("chunks", ["Status", "Price"]);
        var result = await provisioner.EnsureCollectionAsync(withExtraField, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var probe = await provisioner.ProbeAsync("chunks", CancellationToken.None);
        Assert.True(probe.IsSuccess);
        Assert.Equal(withExtraField.Fingerprint, probe.Value.SchemaFingerprint);
    }

    [Fact]
    public async Task EnsureCollectionAsync_ConflictingRoleForSameField_ReturnsCollectionDefinitionConflict()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();
        await provisioner.EnsureCollectionAsync(
            VectorCollectionDefinition.Create(
                "chunks", "test-model", 2, VectorDistanceMetric.Cosine,
                [new VectorFieldDefinition { Name = "Status", Kind = VectorFieldKind.String, Filterable = true }]).Value,
            CancellationToken.None);

        var conflicting = VectorCollectionDefinition.Create(
            "chunks", "test-model", 2, VectorDistanceMetric.Cosine,
            [new VectorFieldDefinition { Name = "Status", Kind = VectorFieldKind.String, Filterable = false }]).Value;

        var result = await provisioner.EnsureCollectionAsync(conflicting, CancellationToken.None);

        Assert.Equal(IntelligenceErrors.CollectionDefinitionConflict("chunks", "Status"), result.Error);
    }

    [Fact]
    public async Task EnsureCollectionAsync_SimulateFailure_ReturnsFailure_AndDoesNotRegister()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner { SimulateFailure = true };

        var result = await provisioner.EnsureCollectionAsync(BuildDefinition("chunks", ["Status"]), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(provisioner.RegisteredCollectionNames);
    }

    [Fact]
    public async Task CollectionExistsAsync_RegisteredAndUnregistered()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();
        await provisioner.EnsureCollectionAsync(BuildDefinition("chunks", ["Status"]), CancellationToken.None);

        Assert.True((await provisioner.CollectionExistsAsync("chunks", CancellationToken.None)).Value);
        Assert.False((await provisioner.CollectionExistsAsync("never-registered", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task DeleteCollectionAsync_AbsentName_IsIdempotent()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();

        var result = await provisioner.DeleteCollectionAsync("never-registered", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteCollectionAsync_RegisteredName_Removes()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();
        await provisioner.EnsureCollectionAsync(BuildDefinition("chunks", ["Status"]), CancellationToken.None);

        var result = await provisioner.DeleteCollectionAsync("chunks", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False((await provisioner.CollectionExistsAsync("chunks", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task DeleteCollectionAsync_SimulateFailure_ReturnsFailure_AndLeavesCollectionRegistered()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();
        await provisioner.EnsureCollectionAsync(BuildDefinition("chunks", ["Status"]), CancellationToken.None);
        provisioner.SimulateFailure = true;

        var result = await provisioner.DeleteCollectionAsync("chunks", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True((await provisioner.CollectionExistsAsync("chunks", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task CutoverAsync_UnregisteredStaging_ReturnsCutoverFailed()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();

        var result = await provisioner.CutoverAsync(
            new VectorCollectionCutoverRequest { StagingCollectionName = "chunks-staging", LiveCollectionName = "chunks" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("intelligence.cutover_failed", result.Error.Code);
    }

    [Fact]
    public async Task CutoverAsync_DeleteStagingAfterCutover_True_PromotesLive_AndRemovesStaging()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();
        await provisioner.EnsureCollectionAsync(BuildDefinition("chunks-staging", ["Status"]), CancellationToken.None);

        var result = await provisioner.CutoverAsync(
            new VectorCollectionCutoverRequest { StagingCollectionName = "chunks-staging", LiveCollectionName = "chunks", DeleteStagingAfterCutover = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True((await provisioner.CollectionExistsAsync("chunks", CancellationToken.None)).Value);
        Assert.False((await provisioner.CollectionExistsAsync("chunks-staging", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task CutoverAsync_DeleteStagingAfterCutover_False_RetainsStaging()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();
        await provisioner.EnsureCollectionAsync(BuildDefinition("chunks-staging", ["Status"]), CancellationToken.None);

        var result = await provisioner.CutoverAsync(
            new VectorCollectionCutoverRequest { StagingCollectionName = "chunks-staging", LiveCollectionName = "chunks", DeleteStagingAfterCutover = false },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True((await provisioner.CollectionExistsAsync("chunks-staging", CancellationToken.None)).Value);
    }

    [Fact]
    public async Task CutoverAsync_SimulateFailure_ReturnsFailure()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();
        await provisioner.EnsureCollectionAsync(BuildDefinition("chunks-staging", ["Status"]), CancellationToken.None);
        provisioner.SimulateFailure = true;

        var result = await provisioner.CutoverAsync(
            new VectorCollectionCutoverRequest { StagingCollectionName = "chunks-staging", LiveCollectionName = "chunks" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ProbeAsync_UnregisteredCollection_ReturnsCollectionNotFound()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();

        var result = await provisioner.ProbeAsync("never-registered", CancellationToken.None);

        Assert.Equal(IntelligenceErrors.CollectionNotFound("never-registered"), result.Error);
    }

    [Fact]
    public async Task ProbeAsync_RegisteredCollection_ReturnsDeterministicHealthyProbe()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();
        var definition = BuildDefinition("chunks", ["Status"]);
        await provisioner.EnsureCollectionAsync(definition, CancellationToken.None);

        var result = await provisioner.ProbeAsync("chunks", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Reachable);
        Assert.True(result.Value.CollectionAddressable);
        Assert.True(result.Value.Queryable);
        Assert.Equal(0, result.Value.VectorCount);
        Assert.Equal(0, result.Value.PendingWriteCount);
        Assert.Equal("in-memory-fake", result.Value.EngineVersion);
        Assert.Equal(definition.Fingerprint, result.Value.SchemaFingerprint);
        Assert.Equal(TimeSpan.Zero, result.Value.Latency);
    }

    [Fact]
    public async Task Reset_ClearsAllRegistrations()
    {
        var provisioner = new InMemoryVectorCollectionProvisioner();
        await provisioner.EnsureCollectionAsync(BuildDefinition("chunks", ["Status"]), CancellationToken.None);

        provisioner.Reset();

        Assert.Empty(provisioner.RegisteredCollectionNames);
    }

    private static VectorCollectionDefinition BuildDefinition(string name, IReadOnlyList<string> fieldNames)
    {
        var fields = fieldNames
            .Select(n => new VectorFieldDefinition { Name = n, Kind = VectorFieldKind.String, Filterable = true })
            .ToArray();
        return VectorCollectionDefinition.Create(name, "test-model", 2, VectorDistanceMetric.Cosine, fields).Value;
    }
}
