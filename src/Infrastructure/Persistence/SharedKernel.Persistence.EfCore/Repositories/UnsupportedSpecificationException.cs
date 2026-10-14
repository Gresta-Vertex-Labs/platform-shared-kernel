using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Thrown when an <see cref="SharedKernel.Domain.Specifications.ISpecification{T}"/> with shapes
/// that have no meaning for a single server-side <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> statement
/// is passed to <see cref="IBulkMutationRepository{TAggregate, TId}"/>.
/// </summary>
internal sealed class UnsupportedSpecificationException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="UnsupportedSpecificationException"/> with the given error.</summary>
    /// <param name="error">The validation error built by the bulk-specification guard.</param>
    public UnsupportedSpecificationException(Error error)
        : base(error)
    {
    }
}
