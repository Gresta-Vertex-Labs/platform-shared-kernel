using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Exceptions;

/// <summary>Thrown when a required aggregate or entity does not exist.</summary>
/// <remarks>
/// <see cref="SharedKernelException.Error"/> is an <see cref="ErrorType.NotFound"/> error (HTTP 404). The
/// hierarchy is <c>SharedKernelException</c>, then <c>DomainException</c>, then this type. Prefer returning a
/// failed result for a lookup the caller expects to miss; throw this when a missing aggregate means the
/// operation cannot continue.
/// </remarks>
public sealed class DomainNotFoundException : DomainException
{
    /// <summary>Creates the exception for the aggregate type and identity that were not found.</summary>
    /// <param name="aggregateType">The type of the missing aggregate.</param>
    /// <param name="aggregateId">The identity that was looked up.</param>
    /// <exception cref="ArgumentNullException"><paramref name="aggregateType"/> or <paramref name="aggregateId"/> is <see langword="null"/>.</exception>
    public DomainNotFoundException(Type aggregateType, object aggregateId)
        : base(CreateError(aggregateType, aggregateId))
    {
        AggregateType = aggregateType;
        AggregateId = aggregateId;
    }

    /// <summary>Gets the type of the missing aggregate.</summary>
    public Type AggregateType { get; }

    /// <summary>Gets the identity that was looked up.</summary>
    public object AggregateId { get; }

    private static Error CreateError(Type aggregateType, object aggregateId)
    {
        ArgumentNullException.ThrowIfNull(aggregateType);
        ArgumentNullException.ThrowIfNull(aggregateId);
        return Error.NotFound(ErrorCodes.NotFound.Default, $"{aggregateType.Name} '{aggregateId}' was not found.");
    }
}
