namespace SharedKernel.Persistence.EfCore.Seeding;

/// <summary>
/// Reports whether the database is ready for the application: every startup migration and seeder registered with
/// <c>MigrateOnStartup()</c> / <c>AddSeeder&lt;T&gt;()</c> has completed. Registered as a singleton by
/// <c>AddSharedKernelPostgres</c>; complete immediately when nothing runs at startup.
/// </summary>
/// <remarks>
/// Readiness checks report not-ready until <see cref="IsCompleted"/>, and background work that needs the schema
/// (the audit sealer, startup self-checks) awaits <see cref="WaitAsync"/>, so a fresh deployment that migrates on
/// startup is never judged against a schema that does not exist yet.
/// </remarks>
public interface IPersistenceStartup
{
    /// <summary>Gets whether every startup migration and seeder has completed successfully.</summary>
    bool IsCompleted { get; }

    /// <summary>Waits until every startup migration and seeder has completed.</summary>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>A task that completes with the startup work, or faults when it failed.</returns>
    Task WaitAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="IPersistenceStartup"/>: one pending step per context that migrates or seeds at startup.
/// </summary>
internal sealed class PersistenceStartupSignal : IPersistenceStartup
{
    private readonly Dictionary<Type, TaskCompletionSource> _steps = [];
    private readonly object _gate = new();

    /// <inheritdoc />
    public bool IsCompleted
    {
        get
        {
            lock (_gate)
                return _steps.Values.All(step => step.Task.IsCompletedSuccessfully);
        }
    }

    /// <inheritdoc />
    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        Task all;
        lock (_gate)
            all = Task.WhenAll(_steps.Values.Select(step => step.Task));

        return all.WaitAsync(cancellationToken);
    }

    /// <summary>Declares that <paramref name="contextType"/> runs startup work; called at registration time.</summary>
    internal void Expect(Type contextType)
    {
        lock (_gate)
            _steps.TryAdd(contextType, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    /// <summary>Marks the startup work of <paramref name="contextType"/> done.</summary>
    internal void Complete(Type contextType) => Step(contextType)?.TrySetResult();

    /// <summary>Marks the startup work of <paramref name="contextType"/> failed; waiters observe the exception.</summary>
    internal void Fail(Type contextType, Exception exception) => Step(contextType)?.TrySetException(exception);

    /// <summary>Marks the startup work of <paramref name="contextType"/> cancelled (the host stopped first).</summary>
    internal void Cancel(Type contextType) => Step(contextType)?.TrySetCanceled();

    private TaskCompletionSource? Step(Type contextType)
    {
        lock (_gate)
            return _steps.GetValueOrDefault(contextType);
    }
}

/// <summary>Finds or adds the one <see cref="PersistenceStartupSignal"/> instance of a service collection.</summary>
internal static class PersistenceStartupSignalRegistration
{
    public static PersistenceStartupSignal GetOrAdd(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        var existing = services
            .Where(d => d.ServiceType == typeof(PersistenceStartupSignal) && !d.IsKeyedService)
            .Select(d => d.ImplementationInstance)
            .OfType<PersistenceStartupSignal>()
            .FirstOrDefault();

        if (existing is not null)
            return existing;

        var signal = new PersistenceStartupSignal();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, signal);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IPersistenceStartup>(services, signal);
        return signal;
    }
}
