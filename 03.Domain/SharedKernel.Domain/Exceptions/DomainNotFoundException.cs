using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Exceptions;

/// <summary>The exception thrown when an aggregate or entity that an operation requires does not exist.</summary>
/// <remarks>
/// <para>
/// <b>Error.</b> <see cref="SharedKernelException.Error"/> is an <see cref="ErrorType.NotFound"/> error, mapped
/// to HTTP 404, with code <see cref="ErrorCodes.NotFound.Default"/> and a message naming the aggregate type
/// and identity, for example <c>Order '3f2b…' was not found.</c>
/// </para>
/// <para>
/// <b>Usage.</b> Throw it when a missing aggregate means the operation cannot continue. For a lookup the
/// caller expects to miss, return a failed result instead. Derives from <see cref="DomainException"/>.
/// </para>
/// <para>
/// <b>Pitfall.</b> The identity is rendered into the message with <see cref="object.ToString"/>, and the
/// message can reach an API response. Never pass an identity whose string form is sensitive.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var order = await orders.GetByIdAsync(orderId, ct)
///     ?? throw new DomainNotFoundException(typeof(Order), orderId);
/// </code>
/// </example>
public sealed class DomainNotFoundException : DomainException
{
    /// <summary>Initializes a new exception for the aggregate type and identity that were not found.</summary>
    /// <param name="aggregateType">The type of the missing aggregate. Must not be null.</param>
    /// <param name="aggregateId">The identity key that was looked up. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="aggregateType"/> or <paramref name="aggregateId"/> is <see langword="null"/>.
    /// </exception>
    public DomainNotFoundException(Type aggregateType, object aggregateId)
        : base(CreateError(aggregateType, aggregateId))
    {
        AggregateType = aggregateType;
        AggregateId = aggregateId;
    }

    /// <summary>Gets the type of the missing aggregate.</summary>
    public Type AggregateType { get; }

    /// <summary>Gets the identity key that was looked up.</summary>
    public object AggregateId { get; }

    private static Error CreateError(Type aggregateType, object aggregateId)
    {
        ArgumentNullException.ThrowIfNull(aggregateType);
        ArgumentNullException.ThrowIfNull(aggregateId);
        return Error.NotFound(ErrorCodes.NotFound.Default, $"{aggregateType.Name} '{aggregateId}' was not found.");
    }
}
