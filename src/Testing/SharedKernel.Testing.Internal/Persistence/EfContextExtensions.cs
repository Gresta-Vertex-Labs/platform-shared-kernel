using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// Extension helpers for exercising EF Core <see cref="DbContext"/> behavior in tests.
/// </summary>
public static class EfContextExtensions
{
    // EF Core's internal per-context service provider does not register the DbContextOptions
    // instance as a resolvable service when a context is constructed standalone (outside an
    // external DI container/AddDbContext) — GetService<DbContextOptions>() and
    // GetService<DbContextOptions<TContext>>() both throw. The constructor-supplied options are
    // captured here, keyed by context instance, so ReloadAsync can rebuild an equivalent context
    // without holding a hard reference that would outlive the source context's lifetime.
    private static readonly ConditionalWeakTable<DbContext, DbContextOptions> OptionsByContext = new();

    /// <summary>
    /// Registers <paramref name="options"/> as the options to use when <see cref="ReloadAsync{T}"/>
    /// needs to construct a fresh context instance equivalent to <paramref name="context"/>.
    /// </summary>
    /// <remarks>
    /// Called automatically by <see cref="TestSharedKernelDbContext"/>'s constructor. Only needed
    /// when calling <see cref="ReloadAsync{T}"/> against a <see cref="DbContext"/> subclass that
    /// does not extend <see cref="TestSharedKernelDbContext"/>.
    /// </remarks>
    /// <param name="context">The context instance the options were used to construct.</param>
    /// <param name="options">The options instance passed to the context's constructor.</param>
    public static void RegisterOptions(DbContext context, DbContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        OptionsByContext.AddOrUpdate(context, options);
    }

    /// <summary>
    /// Detaches every entity currently tracked by <paramref name="context"/>'s
    /// <see cref="ChangeTracker"/>, allowing a fresh same-database reload within one test.
    /// </summary>
    /// <param name="context">The context to detach entities from.</param>
    public static void DetachAll(this DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var entry in context.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    /// <summary>
    /// Loads a fresh copy of <paramref name="entity"/> via a new <see cref="DbContext"/> instance
    /// built from <paramref name="context"/>'s options, proving the entity round-tripped through
    /// persistence.
    /// </summary>
    /// <typeparam name="T">The entity type to reload.</typeparam>
    /// <param name="context">The originating context (used only to source the entity's primary key and options).</param>
    /// <param name="entity">The entity instance to reload by primary key.</param>
    /// <returns>The freshly loaded entity, or <see langword="null"/> if it no longer exists.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="context"/>'s options were never registered — see <see cref="RegisterOptions"/>.
    /// Contexts extending <see cref="TestSharedKernelDbContext"/> register automatically.
    /// </exception>
    public static async Task<T?> ReloadAsync<T>(this DbContext context, T entity)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);

        var entry = context.Entry(entity);
        var keyValues = entry.Metadata.FindPrimaryKey()?.Properties
            .Select(p => entry.Property(p.Name).CurrentValue)
            .ToArray() ?? [];

        await using var freshContext = CreateFreshContext(context);
        return await freshContext.Set<T>().FindAsync(keyValues).ConfigureAwait(false);
    }

    private static DbContext CreateFreshContext(DbContext source)
    {
        if (!OptionsByContext.TryGetValue(source, out var options))
        {
            throw new InvalidOperationException(
                $"No DbContextOptions registered for an instance of '{source.GetType().Name}'. " +
                "EfContextExtensions.ReloadAsync requires the context's options to be registered via " +
                "EfContextExtensions.RegisterOptions — TestSharedKernelDbContext does this automatically " +
                "in its constructor.");
        }

        return (DbContext)Activator.CreateInstance(source.GetType(), options)!;
    }
}
