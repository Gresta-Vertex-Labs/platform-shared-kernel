using HotChocolate.Data.Filters;

namespace SharedKernel.Communication.GraphQL.Types;

/// <summary>
/// Abstract base for all platform GraphQL filter input types.
/// Consuming services extend this class and override <c>Configure</c> to configure
/// visible fields and operations using the snake_case naming convention.
/// </summary>
/// <remarks>
/// <b>Direct registration of <see cref="FilterInputType{T}"/> without this base wrapper is a
/// platform violation.</b> Always extend <see cref="FilterBase{T}"/> instead.
/// </remarks>
/// <typeparam name="T">The entity type being filtered.</typeparam>
public abstract class FilterBase<T> : FilterInputType<T>
{
    // Subclasses override Configure(IFilterInputTypeDescriptor<T>) to set up fields.
}
