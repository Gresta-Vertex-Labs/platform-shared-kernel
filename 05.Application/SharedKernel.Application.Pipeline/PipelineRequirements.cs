using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharedKernel.Application.Pipeline;

/// <summary>A service one registered feature needs, and the call that needs it.</summary>
/// <param name="Feature">What needs it, as written: <c>WithTransactions()</c>.</param>
/// <param name="Service">The service that must be registered.</param>
/// <param name="ServiceKey">The service key, for a keyed service; <see langword="null"/> for an unkeyed one.</param>
internal sealed record PipelineRequirement(string Feature, Type Service, object? ServiceKey = null)
{
    /// <summary>The service as the start-time message names it.</summary>
    public string ServiceName => ServiceKey is null ? Service.FullName! : $"{Service.FullName} (keyed '{ServiceKey}')";
}

/// <summary>
/// Every service the registered pipeline needs. Its presence in a service collection also marks that
/// <c>AddSharedKernelApplication</c> has run there.
/// </summary>
/// <param name="Requirements">The requirements, in the order they were declared.</param>
internal sealed record PipelineRequirements(IReadOnlyList<PipelineRequirement> Requirements);

/// <summary>
/// An options type with no settings: it exists so the seam check runs through <c>ValidateOnStart</c>, when the host
/// starts, after every registration has been made.
/// </summary>
internal sealed class PipelineRequirementsOptions;

/// <summary>
/// Fails the host start when a service the pipeline needs is not registered, naming every missing service and what
/// needs it in one message.
/// </summary>
internal sealed class PipelineRequirementsValidator(PipelineRequirements requirements, IServiceProvider services)
    : IValidateOptions<PipelineRequirementsOptions>
{
    public ValidateOptionsResult Validate(string? name, PipelineRequirementsOptions options)
    {
        var isService = services.GetRequiredService<IServiceProviderIsService>();
        var isKeyedService = services.GetService<IServiceProviderIsKeyedService>();

        var missing = requirements.Requirements
            .Where(requirement => requirement.ServiceKey is null
                ? !isService.IsService(requirement.Service)
                : isKeyedService is null || !isKeyedService.IsKeyedService(requirement.Service, requirement.ServiceKey))
            .GroupBy(requirement => requirement.ServiceName)
            .Select(group =>
                $"{group.Key} (needed by {string.Join(", ", group.Select(r => r.Feature).Distinct())})")
            .ToList();

        if (missing.Count == 0)
            return ValidateOptionsResult.Success;

        return ValidateOptionsResult.Fail(
            "AddSharedKernelApplication: the pipeline needs services that are not registered: " +
            string.Join("; ", missing) +
            ". Register them (before or after AddSharedKernelApplication), or remove what needs them " +
            "(a With… call, or [RequirePermission]).");
    }
}
