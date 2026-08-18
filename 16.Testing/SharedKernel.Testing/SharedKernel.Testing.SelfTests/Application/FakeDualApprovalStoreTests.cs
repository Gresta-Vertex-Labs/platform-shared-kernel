using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.DualApproval;
using SharedKernel.Testing.Application;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Application;

/// <summary>
/// Proves <see cref="FakeDualApprovalStore"/> against the <see cref="IDualApprovalStore"/> contract owned
/// by <c>05.Application.Behaviors</c>.
/// </summary>
public sealed class FakeDualApprovalStoreTests
{
    [Fact]
    public async Task TryGetApprovalAsync_UnrecordedKey_ReturnsNull()
    {
        var store = new FakeDualApprovalStore();

        var result = await store.TryGetApprovalAsync("approval-1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RecordApprovalAsync_ThenTryGetApprovalAsync_RoundTripsApproverIdentity()
    {
        var store = new FakeDualApprovalStore();

        await store.RecordApprovalAsync("approval-1", "approver@test.com", CancellationToken.None);
        var result = await store.TryGetApprovalAsync("approval-1", CancellationToken.None);

        Assert.Equal("approver@test.com", result);
    }

    [Fact]
    public async Task RecordApprovalAsync_SecondCallForSameKey_OverwritesPriorApprover()
    {
        var store = new FakeDualApprovalStore();

        await store.RecordApprovalAsync("approval-1", "first-approver@test.com", CancellationToken.None);
        await store.RecordApprovalAsync("approval-1", "second-approver@test.com", CancellationToken.None);
        var result = await store.TryGetApprovalAsync("approval-1", CancellationToken.None);

        Assert.Equal("second-approver@test.com", result);
    }

    [Fact]
    public async Task SimulateFailure_True_TryGetApprovalAsync_Throws()
    {
        var store = new FakeDualApprovalStore { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.TryGetApprovalAsync("approval-1", CancellationToken.None));
    }

    [Fact]
    public async Task SimulateFailure_True_RecordApprovalAsync_Throws()
    {
        var store = new FakeDualApprovalStore { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RecordApprovalAsync("approval-1", "approver@test.com", CancellationToken.None));
    }

    [Fact]
    public async Task Reset_ClearsRecordedApprovals_ButLeavesSimulateFailureUntouched()
    {
        var store = new FakeDualApprovalStore();
        await store.RecordApprovalAsync("approval-1", "approver@test.com", CancellationToken.None);
        store.SimulateFailure = true;

        store.Reset();

        Assert.True(store.SimulateFailure);
        store.SimulateFailure = false;
        Assert.Null(await store.TryGetApprovalAsync("approval-1", CancellationToken.None));
    }

    [Fact]
    public void ImplementsIDualApprovalStore()
    {
        IDualApprovalStore store = new FakeDualApprovalStore();

        Assert.IsType<FakeDualApprovalStore>(store);
    }
}

/// <summary>
/// Proves <see cref="ApplicationServiceCollectionExtensions.AddFakeDualApprovalStore"/>.
/// </summary>
public sealed class AddFakeDualApprovalStoreTests
{
    [Fact]
    public void AddFakeDualApprovalStore_RegistersFakeDualApprovalStore()
    {
        var provider = BuildProvider();

        Assert.IsType<FakeDualApprovalStore>(provider.GetRequiredService<IDualApprovalStore>());
    }

    [Fact]
    public void AddFakeDualApprovalStore_RegistersAsSingleton()
    {
        var provider = BuildProvider();

        Assert.Same(
            provider.GetRequiredService<IDualApprovalStore>(),
            provider.GetRequiredService<IDualApprovalStore>());
    }

    [Fact]
    public void AddFakeDualApprovalStore_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddFakeDualApprovalStore());

    [Fact]
    public void AddFakeDualApprovalStore_IsNotBundledInto_AddFakeApplicationBehaviorServices()
    {
        var services = new ServiceCollection();
        services.AddFakeApplicationBehaviorServices();
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IDualApprovalStore>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddFakeDualApprovalStore();
        return services.BuildServiceProvider();
    }
}
