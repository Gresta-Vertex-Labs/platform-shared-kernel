using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.Persistence.EfCore.Tests.UnitOfWork;

// Retry-forcing fixtures (formerly shared with the retired PersistenceRetryDiagnosticListenerTests).

/// <summary>Serializes test classes that force genuine EF Core execution-strategy retries.</summary>
[CollectionDefinition("RetryDiagnostics")]
public sealed class RetryDiagnosticsCollection;


// ---------------------------------------------------------------------------
// Test-local plain DbContext (this test targets the diagnostic listener in isolation —
// no SharedKernelDbContext/persistence interceptors are needed).
// ---------------------------------------------------------------------------

public sealed class RetryDiagListenerTestDbContext : SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext
{
    public DbSet<RetryDiagListenerTestItem> Items => Set<RetryDiagListenerTestItem>();

    public RetryDiagListenerTestDbContext(
        DbContextOptions<RetryDiagListenerTestDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RetryDiagListenerTestItem>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
        });
    }
}

public sealed class RetryDiagListenerTestItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Retry-forcing execution strategy — mirrors the technique independently verified against the
// real EF Core 10.0.5 package before this test was written.
// ---------------------------------------------------------------------------

internal sealed class AlwaysRetryStrategy : ExecutionStrategy
{
    public AlwaysRetryStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, maxRetryCount: 5, maxRetryDelay: TimeSpan.FromMilliseconds(1))
    {
    }

    protected override bool ShouldRetryOn(Exception exception) => exception is InvalidOperationException;
}

internal sealed class AlwaysRetryStrategyFactory : IExecutionStrategyFactory
{
    private readonly ExecutionStrategyDependencies _dependencies;

    public AlwaysRetryStrategyFactory(ExecutionStrategyDependencies dependencies) => _dependencies = dependencies;

    public IExecutionStrategy Create() => new AlwaysRetryStrategy(_dependencies);
}

// Throws a simulated transient failure for the first `failuresBeforeSuccess` INSERT attempts,
// then lets subsequent attempts through — both the reader and non-query paths are overridden
// since SQLite/Npgsql route store-generated-value writes through ExecuteReader, not
// ExecuteNonQuery (a documented gotcha in this domain's own test-writing history).
internal sealed class FaultInjectingInterceptor : DbCommandInterceptor
{
    private readonly int _failuresBeforeSuccess;
    private int _attempts;

    public FaultInjectingInterceptor(int failuresBeforeSuccess) => _failuresBeforeSuccess = failuresBeforeSuccess;

    public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
        System.Data.Common.DbCommand command,
        CommandEventData eventData,
        InterceptionResult<System.Data.Common.DbDataReader> result)
    {
        MaybeThrow(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command,
        CommandEventData eventData,
        InterceptionResult<System.Data.Common.DbDataReader> result,
        CancellationToken ct = default)
    {
        MaybeThrow(command);
        return await base.ReaderExecutingAsync(command, eventData, result, ct);
    }

    private void MaybeThrow(System.Data.Common.DbCommand command)
    {
        if (!command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
            return;

        if (Interlocked.Increment(ref _attempts) <= _failuresBeforeSuccess)
            throw new InvalidOperationException("Simulated transient database failure.");
    }
}
