using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Scheduling.Registry;

namespace SharedKernel.Scheduling.Extensions;

/// <summary>
/// The fluent builder returned by <see cref="SchedulingServiceCollectionExtensions.AddSharedKernelScheduling"/>,
/// combining the job registration surface (<see cref="IScheduledJobRegistry"/>) with access to the
/// underlying <see cref="IServiceCollection"/> for further composition.
/// </summary>
public interface ISchedulingBuilder : IScheduledJobRegistry
{
    /// <summary>Gets the service collection this builder registers into.</summary>
    IServiceCollection Services { get; }
}
