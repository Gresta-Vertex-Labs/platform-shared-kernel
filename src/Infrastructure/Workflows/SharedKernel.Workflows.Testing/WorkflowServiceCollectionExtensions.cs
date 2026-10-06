using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Workflows.Temporal.Dispatch;

namespace SharedKernel.Testing.Workflows;

/// <summary>
/// DI registration convenience extensions for the <c>Workflows/</c> in-memory test doubles.
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemoryWorkflowDispatcher"/> as <see cref="IWorkflowDispatcher"/>.
    /// </summary>
    /// <remarks>
    /// Registered as a <b>singleton</b> -- a deliberate deviation from the real production
    /// <c>IWorkflowDispatcher</c> registration, which is <b>scoped</b> because it captures the ambient
    /// correlation/tenant context of the current request (see <c>src/Infrastructure/Workflows/CLAUDE.md</c>'s DI
    /// Registration section). The same recorded-history instance must outlive the system-under-test's
    /// DI scope so post-hoc <see cref="InMemoryWorkflowDispatcher.ShouldHaveStarted{TWorkflow}"/>-style
    /// assertions can run after the action completes -- mirrors <c>AddInMemoryMessageBus()</c>/
    /// <c>AddInMemorySearchIndex{TDocument}</c>'s identical documented singleton-lifetime deviation.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    public static IServiceCollection AddInMemoryWorkflowDispatcher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton<IWorkflowDispatcher, InMemoryWorkflowDispatcher>();
    }
}
