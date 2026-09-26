using Microsoft.Extensions.Options;

namespace SharedKernel.Presentation.GraphQL.Options;

/// <summary>
/// Configuration for the platform-standard HotChocolate GraphQL server registered via
/// <c>AddSharedKernelGraphQL</c>.
/// </summary>
public sealed class GraphQLOptions
{
    /// <summary>Whether to enable filtering via <c>HotChocolate.Data</c>. Default: <c>true</c>.</summary>
    public bool EnableFiltering { get; set; } = true;

    /// <summary>Whether to enable sorting via <c>HotChocolate.Data</c>. Default: <c>true</c>.</summary>
    public bool EnableSorting { get; set; } = true;

    /// <summary>Whether to enable offset and cursor pagination. Default: <c>true</c>.</summary>
    public bool EnablePaging { get; set; } = true;

    /// <summary>
    /// Maximum number of items a paging argument may request. Default: 100.
    /// Hard cap is 500 — the validator rejects values above this.
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Whether schema introspection is allowed. Default: <c>true</c>.
    /// Services must set this to <c>false</c> in production environments.
    /// </summary>
    public bool AllowIntrospection { get; set; } = true;
}

/// <summary>
/// Validates <see cref="GraphQLOptions"/> at startup. Rejects <see cref="GraphQLOptions.MaxPageSize"/>
/// values greater than 500.
/// </summary>
internal sealed class GraphQLOptionsValidator : IValidateOptions<GraphQLOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GraphQLOptions options)
    {
        if (options.MaxPageSize > 500)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(GraphQLOptions.MaxPageSize)} must not exceed 500. " +
                $"Current value: {options.MaxPageSize}. " +
                "Any override beyond 500 requires documented justification in the consuming service.");
        }

        if (options.MaxPageSize < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(GraphQLOptions.MaxPageSize)} must be at least 1. " +
                $"Current value: {options.MaxPageSize}.");
        }

        return ValidateOptionsResult.Success;
    }
}
