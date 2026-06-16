using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Communication.GraphQL.Options;

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
    [Range(1, 500)]
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Whether schema introspection is allowed. Default: <c>true</c>.
    /// Services must set this to <c>false</c> in production environments.
    /// </summary>
    public bool AllowIntrospection { get; set; } = true;
}
