using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Testing.Workflows;
using SharedKernel.Workflows.Temporal.Dispatch;

namespace SharedKernel.Testing.SelfTests.Workflows;

/// <summary>
/// Proves the <c>Workflows/</c> DI convenience extension (<c>AddInMemoryWorkflowDispatcher</c>
/// registration shape, including its deliberate singleton-lifetime deviation from the real scoped
/// <c>IWorkflowDispatcher</c> registration) -- no consuming domain has adopted this fake yet, so this
/// self-test is the only behavioral proof today, per the SelfTests routing rule (T-55/P-288/WO-046).
/// </summary>
public sealed class WorkflowServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInMemoryWorkflowDispatcher_ResolvesIWorkflowDispatcher_AsInMemoryWorkflowDispatcher()
    {
        var services = new ServiceCollection();
        services.AddInMemoryWorkflowDispatcher();
        var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryWorkflowDispatcher>(provider.GetRequiredService<IWorkflowDispatcher>());
    }

    [Fact]
    public void AddInMemoryWorkflowDispatcher_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInMemoryWorkflowDispatcher());

    [Fact]
    public void AddInMemoryWorkflowDispatcher_IsRegisteredAsSingleton_ProvenViaScopedResolution()
    {
        // Deliberate deviation from the real production SCOPED IWorkflowDispatcher lifetime: the
        // fake must be a singleton so the same recorded-history instance survives across DI scopes,
        // letting a post-hoc ShouldHaveStarted/ShouldHaveSignalled assertion run after the
        // system-under-test's own scope has ended.
        var services = new ServiceCollection();
        services.AddInMemoryWorkflowDispatcher();
        var provider = services.BuildServiceProvider();

        IWorkflowDispatcher first;
        IWorkflowDispatcher second;
        using (var scope1 = provider.CreateScope())
        {
            first = scope1.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        }

        using (var scope2 = provider.CreateScope())
        {
            second = scope2.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        }

        Assert.Same(first, second);
    }

    [Fact]
    public void AddInMemoryWorkflowDispatcher_RepeatedInterfaceResolution_ReturnsSameSingletonInstance()
    {
        // Unlike some sibling AddInMemoryX() extensions elsewhere in this package,
        // AddInMemoryWorkflowDispatcher() registers InMemoryWorkflowDispatcher ONLY as
        // IWorkflowDispatcher -- the concrete type is not separately resolvable from the container
        // (confirmed against the shipped WorkflowServiceCollectionExtensions source). The singleton
        // proof is therefore made purely through the interface.
        var services = new ServiceCollection();
        services.AddInMemoryWorkflowDispatcher();
        var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IWorkflowDispatcher>();
        var second = provider.GetRequiredService<IWorkflowDispatcher>();

        Assert.Same(first, second);
        Assert.IsType<InMemoryWorkflowDispatcher>(first);
    }

    [Fact]
    public async Task AddInMemoryWorkflowDispatcher_HistorySurvivesAcrossDiScopes()
    {
        // The behavioral consequence of the singleton lifetime: a workflow started in one DI scope
        // is visible to an assertion made after that scope has been disposed.
        var services = new ServiceCollection();
        services.AddInMemoryWorkflowDispatcher();
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var dispatcher = (InMemoryWorkflowDispatcher)scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
            await dispatcher.StartAsync<SampleWorkflow>(WorkflowsTestFixtures.ValidOptions(), TenantScope.Of("tenant-a"));
        }

        var afterScope = (InMemoryWorkflowDispatcher)provider.GetRequiredService<IWorkflowDispatcher>();
        afterScope.ShouldHaveStartedOnce<SampleWorkflow>();
    }
}
