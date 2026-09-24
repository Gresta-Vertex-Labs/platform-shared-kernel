using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharedKernel.Application.Pipeline;

/// <summary>A service one opted-in behavior needs, and the builder call that opted it in.</summary>
/// <param name="Feature">The builder call that needs it, as written: <c>WithTransactions()</c>.</param>
/// <param name="Service">The service that must be registered.</param>
internal sealed record PipelineRequirement(string Feature, Type Service);

/// <summary>
/// Every service the registered pipeline needs. Its presence in a service collection also marks
/// that <c>AddSharedKernelApplication</c> has run there.
/// </summary>
/// <param name="Requirements">The requirements, in the order they were declared.</param>
internal sealed record PipelineRequirements(IReadOnlyList<PipelineRequirement> Requirements);

/// <summary>
/// An options type with no settings: it exists so the seam check runs through
/// <c>ValidateOnStart</c>, when the host starts, after every registration has been made.
/// </summary>
internal sealed class PipelineRequirementsOptions;

/// <summary>
/// Fails the host start when a service the pipeline needs is not registered, naming every missing
/// service and the builder calls that need it in one message.
/// </summary>
internal sealed class PipelineRequirementsValidator(
    PipelineRequirements requirements,
    IServiceProviderIsService isService)
    : IValidateOptions<PipelineRequirementsOptions>
{
    public ValidateOptionsResult Validate(string? name, PipelineRequirementsOptions options)
    {
        var missing = requirements.Requirements
            .Where(requirement => !isService.IsService(requirement.Service))
            .GroupBy(requirement => requirement.Service)
            .Select(group =>
                $"{group.Key.FullName} (needed by {string.Join(", ", group.Select(r => r.Feature).Distinct())})")
            .ToList();

        if (missing.Count == 0)
            return ValidateOptionsResult.Success;

        return ValidateOptionsResult.Fail(
            "AddSharedKernelApplication: the pipeline needs services that are not registered: " +
            string.Join("; ", missing) +
            ". Register them (before or after AddSharedKernelApplication), or remove the With… call " +
            "that needs them.");
    }
}
