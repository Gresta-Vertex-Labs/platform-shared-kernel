using HotChocolate.Data.Sorting;

namespace SharedKernel.Presentation.GraphQL.Types;

/// <summary>
/// Abstract base for all platform GraphQL sort input types.
/// Consuming services override <c>Configure</c> to configure sortable fields.
/// The platform snake_case naming convention is applied automatically via
/// <see cref="Conventions.SharedKernelFilterConvention"/>.
/// </summary>
/// <remarks>
/// <b>Direct registration of <see cref="SortInputType{T}"/> without this base wrapper is a
/// platform violation.</b> Always extend <see cref="SortBase{T}"/> instead, then register
/// the concrete subclass via <c>AddType&lt;TSortType&gt;()</c> on the
/// <c>IRequestExecutorBuilder</c>.
/// </remarks>
/// <typeparam name="T">The entity type being sorted.</typeparam>
public abstract class SortBase<T> : SortInputType<T>
{
    // Subclasses override Configure(ISortInputTypeDescriptor<T>) to set up sortable fields.
    // The SharedKernelFilterConvention enforces snake_case operation names globally.
}
