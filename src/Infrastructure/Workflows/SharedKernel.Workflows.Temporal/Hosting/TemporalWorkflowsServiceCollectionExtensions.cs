using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Workflows.Temporal.Hosting;

/// <summary>
/// The entry point for composing <c>17.Workflows</c> into a host's <see cref="IServiceCollection"/>.
/// </summary>
public static class TemporalWorkflowsServiceCollectionExtensions
{
    /// <summary>
    /// Begins a fluent Temporal workflows composition, bound to the
    /// <c>Workflows:Temporal</c> configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    public static ITemporalWorkflowsBuilder AddSharedKernelTemporalWorkflows(
        this IServiceCollection services,
        IConfiguration configuration)
        => new TemporalWorkflowsBuilder(services, configuration);
}
