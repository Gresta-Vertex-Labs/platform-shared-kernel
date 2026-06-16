using HotChocolate.Data.Sorting;

namespace SharedKernel.Communication.GraphQL.Types;

/// <summary>
/// Abstract base for all platform GraphQL sort input types.
/// Consuming services extend this class and override <c>Configure</c> to configure
/// sortable fields using the snake_case naming convention.
/// </summary>
/// <remarks>
/// <b>Direct registration of <see cref="SortInputType{T}"/> without this base wrapper is a
/// platform violation.</b> Always extend <see cref="SortBase{T}"/> instead.
/// </remarks>
/// <typeparam name="T">The entity type being sorted.</typeparam>
public abstract class SortBase<T> : SortInputType<T>
{
    // Subclasses override Configure(ISortInputTypeDescriptor<T>) to set up fields.
}
