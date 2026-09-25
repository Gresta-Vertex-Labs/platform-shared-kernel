using HotChocolate.Data.Filters;

namespace SharedKernel.Presentation.GraphQL.Conventions;

/// <summary>
/// Platform-standard HotChocolate filter convention: adds default filter operations and applies
/// snake_case binding names to all string, numeric, and date filter fields.
/// Registered as <c>IFilterConvention</c> by <c>AddSharedKernelGraphQL</c>.
/// </summary>
internal sealed class SharedKernelFilterConvention : FilterConvention
{
    /// <inheritdoc />
    protected override void Configure(IFilterConventionDescriptor descriptor)
    {
        // Wire the default queryable provider and all standard operations
        descriptor.AddDefaults();

        // snake_case equality / inequality (string, numeric, date)
        descriptor.Operation(DefaultFilterOperations.Equals).Name("eq");
        descriptor.Operation(DefaultFilterOperations.NotEquals).Name("neq");

        // string-specific operations
        descriptor.Operation(DefaultFilterOperations.Contains).Name("contains");
        descriptor.Operation(DefaultFilterOperations.NotContains).Name("not_contains");
        descriptor.Operation(DefaultFilterOperations.StartsWith).Name("starts_with");
        descriptor.Operation(DefaultFilterOperations.NotStartsWith).Name("not_starts_with");
        descriptor.Operation(DefaultFilterOperations.EndsWith).Name("ends_with");
        descriptor.Operation(DefaultFilterOperations.NotEndsWith).Name("not_ends_with");

        // set membership
        descriptor.Operation(DefaultFilterOperations.In).Name("in");
        descriptor.Operation(DefaultFilterOperations.NotIn).Name("not_in");

        // numeric / date comparison (HC names these LowerThan / GreaterThan)
        descriptor.Operation(DefaultFilterOperations.GreaterThan).Name("gt");
        descriptor.Operation(DefaultFilterOperations.GreaterThanOrEquals).Name("gte");
        descriptor.Operation(DefaultFilterOperations.LowerThan).Name("lt");
        descriptor.Operation(DefaultFilterOperations.LowerThanOrEquals).Name("lte");
    }
}
