namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// A strongly-typed identifier: a named wrapper around a primitive key, so one kind of identifier
/// cannot be passed where another is expected.
/// </summary>
/// <typeparam name="TValue">
/// The underlying key type, such as <see cref="Guid"/> or <see cref="long"/>. Must be non-null.
/// </typeparam>
/// <remarks>
/// <b>Usage.</b> Derive from <see cref="SharedKernel.Domain.StronglyTypedIds.StronglyTypedId{TValue}"/>
/// rather than implementing this interface directly. The base record supplies equality by type and
/// value, rejects a <see langword="null"/> value, and converts to <typeparamref name="TValue"/> only
/// explicitly, so an identifier never flows silently into a parameter of the underlying type.
/// </remarks>
public interface IStronglyTypedId<TValue> where TValue : notnull
{
    /// <summary>Gets the underlying key value.</summary>
    TValue Value { get; }
}
