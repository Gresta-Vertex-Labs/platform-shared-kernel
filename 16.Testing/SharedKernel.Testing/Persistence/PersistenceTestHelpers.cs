using Microsoft.EntityFrameworkCore;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// Static assertion helpers for verifying EF Core <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker"/>
/// state in tests.
/// </summary>
public static class PersistenceTestHelpers
{
    /// <summary>Asserts that <paramref name="entity"/> is currently tracked by <paramref name="context"/>.</summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="context">The context to inspect.</param>
    /// <param name="entity">The entity instance to check.</param>
    /// <exception cref="InvalidOperationException"><paramref name="entity"/>'s tracking state is <see cref="EntityState.Detached"/>.</exception>
    public static void AssertEntityTracked<T>(DbContext context, T entity)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);

        var state = context.Entry(entity).State;
        if (state == EntityState.Detached)
        {
            throw new InvalidOperationException(
                $"Expected entity of type '{typeof(T).Name}' to be tracked, but its ChangeTracker state is Detached.");
        }
    }

    /// <summary>Asserts that <paramref name="entity"/> is NOT currently tracked by <paramref name="context"/>.</summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="context">The context to inspect.</param>
    /// <param name="entity">The entity instance to check.</param>
    /// <exception cref="InvalidOperationException"><paramref name="entity"/> is tracked in any non-<see cref="EntityState.Detached"/> state.</exception>
    public static void AssertEntityNotTracked<T>(DbContext context, T entity)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);

        var state = context.Entry(entity).State;
        if (state != EntityState.Detached)
        {
            throw new InvalidOperationException(
                $"Expected entity of type '{typeof(T).Name}' not to be tracked, but its ChangeTracker state is {state}.");
        }
    }
}
