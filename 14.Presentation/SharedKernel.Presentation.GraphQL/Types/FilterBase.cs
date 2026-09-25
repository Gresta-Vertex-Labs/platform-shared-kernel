using HotChocolate.Data.Filters;

namespace SharedKernel.Presentation.GraphQL.Types;

/// <summary>
/// Abstract base for all platform GraphQL filter input types.
/// Consuming services override <c>Configure</c> to configure visible fields and operations.
/// The platform snake_case naming convention is applied automatically via
/// <see cref="Conventions.SharedKernelFilterConvention"/>.
/// </summary>
/// <remarks>
/// <b>Direct registration of <see cref="FilterInputType{T}"/> without this base wrapper is a
/// platform violation.</b> Always extend <see cref="FilterBase{T}"/> instead, then register
/// the concrete subclass via <c>AddType&lt;TFilterType&gt;()</c> on the
/// <c>IRequestExecutorBuilder</c>.
/// </remarks>
/// <typeparam name="T">The entity type being filtered.</typeparam>
public abstract class FilterBase<T> : FilterInputType<T>
{
    // Subclasses override Configure(IFilterInputTypeDescriptor<T>) to set up visible fields
    // and operations. The SharedKernelFilterConvention enforces snake_case operation names
    // globally — individual field binding names follow the INamingConventions convention.
}
