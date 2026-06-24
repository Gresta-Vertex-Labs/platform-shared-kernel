using Bogus;
using SharedKernel.Domain.Entities;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Testing.Fakers;

/// <summary>
/// Abstract <see cref="Faker{T}"/> base for generating <see cref="Entity{TId}"/>-derived test
/// instances.
/// </summary>
/// <typeparam name="TEntity">The entity type being faked.</typeparam>
/// <typeparam name="TId">The entity's identity key type.</typeparam>
/// <remarks>
/// This is an abstract base only — not a complete auto-faker. Concrete fakers in consuming test
/// projects declare their own <c>RuleFor(...)</c> definitions because domain invariants must be
/// respected; this base does not infer them.
/// </remarks>
public abstract class EntityFaker<TEntity, TId> : Faker<TEntity>
    where TEntity : Entity<TId>
    where TId : notnull
{
    /// <summary>
    /// Gets the <see cref="IClock"/> most recently supplied via <see cref="WithClock"/>, or
    /// <see langword="null"/> when none has been configured.
    /// </summary>
    protected IClock? Clock { get; private set; }

    /// <summary>
    /// Wires a clock for use by concrete <c>RuleFor(...)</c> declarations that need to construct
    /// entities with deterministic timestamps.
    /// </summary>
    /// <param name="clock">The clock to use during construction.</param>
    /// <returns>This faker, for fluent chaining.</returns>
    public EntityFaker<TEntity, TId> WithClock(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Clock = clock;
        return this;
    }
}
