using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// Gives every aggregate EF Core materializes from the database the application's <see cref="IClock"/>.
/// </summary>
/// <remarks>
/// <para>
/// EF Core creates an aggregate through its parameterless constructor, which cannot receive a clock. Without
/// one, the aggregate throws <see cref="InvalidOperationException"/> the first time it raises a timestamped
/// domain event or soft-deletes. This interceptor calls <see cref="IHasClock.AttachClock"/> on each
/// materialized <see cref="IHasClock"/> instance, for tracking and no-tracking queries alike. An aggregate
/// that already has a clock is left unchanged.
/// </para>
/// <para>
/// <see cref="SharedKernelDbContext"/> registers it automatically, and the clock comes from the context
/// being materialized. Construct it with a clock only for a <c>DbContext</c> that does not derive from
/// <see cref="SharedKernelDbContext"/>.
/// </para>
/// <para>
/// <b>Register one instance for the application's lifetime.</b> EF Core treats materialization interceptors
/// as singleton interceptors that form part of the key for its internal service provider, so adding a new
/// instance to every context builds a new internal service provider each time.
/// </para>
/// </remarks>
public sealed class DomainClockMaterializationInterceptor : IMaterializationInterceptor
{
    private readonly IClock? _clock;

    /// <summary>Creates an interceptor that attaches <paramref name="clock"/> to every materialized aggregate.</summary>
    /// <param name="clock">The clock to attach.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    public DomainClockMaterializationInterceptor(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    private DomainClockMaterializationInterceptor()
    {
    }

    /// <summary>
    /// The shared instance <see cref="SharedKernelDbContext"/> registers, which reads the clock from the context
    /// performing the materialization.
    /// </summary>
    internal static DomainClockMaterializationInterceptor FromContext { get; } = new();

    /// <inheritdoc/>
    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (entity is IHasClock { IsClockAttached: false } hasClock)
        {
            var clock = _clock ?? (materializationData.Context as SharedKernelDbContext)?.Clock;
            if (clock is not null)
                hasClock.AttachClock(clock);
        }

        return entity;
    }
}
