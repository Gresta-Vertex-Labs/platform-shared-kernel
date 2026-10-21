using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Seeding;

/// <summary>
/// The lock, signal and log sequence every startup hosted service of a context shares: take the cross-replica
/// <see cref="IMigrationLock"/> (keyed by the context type's full name), run the work, report completion,
/// cancellation or failure to the <see cref="PersistenceStartupSignal"/>, and release the lock.
/// </summary>
internal static class StartupSequenceRunner
{
    public static async Task RunAsync(
        IServiceProvider services,
        Type contextType,
        TimeSpan lockTimeout,
        ILogger logger,
        PersistenceStartupSignal? startupSignal,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        var contextTypeName = contextType.Name;
        var lockKey = contextType.FullName ?? contextType.Name;
        var migrationLock = services.GetService<IMigrationLock>();

        IAsyncDisposable? lockHandle = null;

        EfCoreLog.MigrationAndSeedStarted(logger, contextTypeName);

        try
        {
            if (migrationLock is not null)
            {
                lockHandle = await migrationLock.AcquireAsync(lockKey, lockTimeout, cancellationToken);
                EfCoreLog.AdvisoryLockAcquired(logger, contextTypeName);
            }
            else
            {
                EfCoreLog.NoMigrationLockRegistered(logger, contextTypeName);
            }

            await work(cancellationToken);

            EfCoreLog.MigrationAndSeedCompleted(logger, contextTypeName);
            startupSignal?.Complete(contextType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            startupSignal?.Cancel(contextType);
            throw;
        }
        catch (Exception ex)
        {
            EfCoreLog.MigrationAndSeedFailed(logger, contextTypeName, ex);
            startupSignal?.Fail(contextType, ex);
            throw;
        }
        finally
        {
            if (lockHandle is not null)
            {
                await lockHandle.DisposeAsync();
                EfCoreLog.AdvisoryLockReleased(logger, contextTypeName);
            }
        }
    }
}
