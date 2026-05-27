using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Exceptions;

/// <summary>
/// Thrown when a required aggregate or entity cannot be found.
/// </summary>
/// <remarks>
/// <para>
/// Hierarchy: <c>Exception</c> → <c>SharedKernelException</c> → <c>DomainException</c>
/// → <c>DomainNotFoundException</c>.
/// Catching <c>DomainException</c> catches this; catching <c>SharedKernelException</c>
/// also catches this.
/// </para>
/// <para>
/// <see cref="SharedKernelException.Error"/> carries <c>ErrorType.NotFound</c> — maps to HTTP 404
/// at the presentation layer.
/// </para>
/// </remarks>
public sealed class DomainNotFoundException : DomainException
{
    /// <summary>
    /// Initialises a new <see cref="DomainNotFoundException"/> for the specified
    /// aggregate type and identity.
    /// </summary>
    /// <param name="aggregateType">The CLR type of the aggregate that was not found.</param>
    /// <param name="aggregateId">The identifier that was searched for.</param>
    public DomainNotFoundException(Type aggregateType, object aggregateId)
        : base(Error.NotFound(
            ErrorCodes.NotFound.Default,
            $"Entity of type '{aggregateType.Name}' with id '{aggregateId}' was not found."))
    {
        AggregateType = aggregateType;
        AggregateId = aggregateId;
    }

    /// <summary>Gets the CLR type of the aggregate that was not found.</summary>
    public Type AggregateType { get; }

    /// <summary>Gets the identifier that was searched for.</summary>
    public object AggregateId { get; }
}
