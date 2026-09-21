using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Thrown when an <see cref="SharedKernel.Domain.Specifications.ISpecification{T}"/> with shapes
/// that have no meaning for a single server-side <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> statement
/// is passed to <see cref="IBulkMutationRepository{TAggregate, TId}"/>.
/// </summary>
internal sealed class UnsupportedSpecificationException : SharedKernelException
{
    /// <summary>
    /// Initialises a new <see cref="UnsupportedSpecificationException"/> with the given reason.
    /// </summary>
    /// <param name="reason">A human-readable description of which specification shape is unsupported.</param>
    public UnsupportedSpecificationException(string reason)
        : base(
            $"The specification cannot be used with bulk mutation operations: {reason}",
            Error.Validation(
                "Persistence.BulkMutation.UnsupportedSpecification",
                $"The specification cannot be used with bulk mutation operations: {reason}"))
    {
    }
}
