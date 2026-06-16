using HotChocolate.Data.Filters;

namespace SharedKernel.Communication.GraphQL.Conventions;

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
        // TODO: implement
        throw new NotImplementedException();
    }
}
