namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marker for a value object: a domain object with no identity, compared by its values.
/// </summary>
/// <remarks>
/// <b>Usage.</b> Extend <see cref="SharedKernel.Domain.ValueObjects.ValueObject"/> rather than
/// implementing this interface directly. The base class supplies equality over its equality components
/// and validation through <c>EnsureValid()</c>; the marker alone guarantees neither.
/// </remarks>
public interface IValueObject
{
}
