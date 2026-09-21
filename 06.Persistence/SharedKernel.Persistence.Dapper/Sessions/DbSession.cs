using System.Data.Common;
using Dapper;
using Microsoft.Extensions.Logging;
using SharedKernel.Persistence.Dapper.Diagnostics;

namespace SharedKernel.Persistence.Dapper.Sessions;

/// <summary>The <see cref="IDbSession"/> returned by <see cref="DbSessionFactory"/>.</summary>
internal sealed class DbSession(
    DbConnection connection,
    DbTransaction transaction,
    bool owned,
    bool readOnly,
    Guid? tenantId,
    int? commandTimeoutSeconds,
    ILogger logger) : IDbSession
{
    private bool _completed;
    private bool _disposed;

    public DbConnection Connection { get; } = connection;

    public DbTransaction Transaction { get; } = transaction;

    public bool IsEnlisted => !owned;

    public bool IsReadOnly => readOnly;

    public Guid? TenantId => tenantId;

    public Guid RequireTenantId() =>
        tenantId ?? throw new InvalidOperationException("No tenant is resolved for the current caller.");

    public CommandDefinition Command(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new CommandDefinition(
            sql, parameters, Transaction, commandTimeoutSeconds, cancellationToken: cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!owned || _completed)
            return;

        await Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (!owned)
            return;

        try
        {
            if (!_completed)
                await Transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            // A broken connection cannot roll back; the server discards the transaction anyway. Never let
            // this replace the exception that is probably unwinding the caller.
            logger.SessionRollbackFailed(exception);
        }
        finally
        {
            await Transaction.DisposeAsync().ConfigureAwait(false);
            await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
