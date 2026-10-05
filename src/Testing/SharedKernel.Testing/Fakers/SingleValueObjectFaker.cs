using Bogus;
using SharedKernel.Domain.ValueObjects;

namespace SharedKernel.Testing.Fakers;

/// <summary>
/// Abstract <see cref="Faker{T}"/> base for generating <see cref="SingleValueObject{TValue}"/>-derived
/// test instances.
/// </summary>
/// <typeparam name="TValueObject">The concrete single-value-object type being faked.</typeparam>
/// <typeparam name="TValue">The wrapped primitive value type.</typeparam>
public abstract class SingleValueObjectFaker<TValueObject, TValue> : Faker<TValueObject>
    where TValueObject : SingleValueObject<TValue>
    where TValue : notnull
{
    /// <summary>
    /// Configures every generated instance to wrap the fixed <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The fixed value every generated instance should wrap.</param>
    /// <returns>This faker, for fluent chaining.</returns>
    public SingleValueObjectFaker<TValueObject, TValue> WithValue(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        CustomInstantiator(_ => CreateFrom(value));
        return this;
    }

    /// <summary>
    /// Configures every generated instance to wrap a value produced by <paramref name="generator"/>.
    /// </summary>
    /// <param name="generator">A delegate producing the wrapped value from a <see cref="Bogus.Faker"/>.</param>
    /// <returns>This faker, for fluent chaining.</returns>
    public SingleValueObjectFaker<TValueObject, TValue> WithRandomValue(Func<Bogus.Faker, TValue> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);

        CustomInstantiator(f => CreateFrom(generator(f)));
        return this;
    }

    /// <summary>
    /// Constructs a <typeparamref name="TValueObject"/> instance wrapping <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// The default implementation invokes the public constructor accepting a single
    /// <typeparamref name="TValue"/> parameter, which is the documented shape for
    /// <see cref="SingleValueObject{TValue}"/> subclasses. Override when the concrete type's
    /// constructor differs.
    /// </remarks>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <typeparamref name="TValueObject"/> instance.</returns>
    protected virtual TValueObject CreateFrom(TValue value) =>
        (TValueObject)Activator.CreateInstance(typeof(TValueObject), value)!;
}
