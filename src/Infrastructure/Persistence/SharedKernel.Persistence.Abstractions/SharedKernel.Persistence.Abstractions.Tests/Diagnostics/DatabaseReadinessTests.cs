using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Diagnostics;

namespace SharedKernel.Persistence.Abstractions.Tests.Diagnostics;

// ---------------------------------------------------------------------------
// Minimal in-memory DbConnection / DbCommand fakes (retyped from
// IDbConnection/IDbCommand to the real System.Data.Common abstract base classes, since
// IDbConnectionFactory.CreateConnectionAsync now returns DbConnection directly and there is no
// longer an IDbConnection-shaped implementation path at all).
// ---------------------------------------------------------------------------

file sealed class FakeParameterCollection : DbParameterCollection
{
    private readonly List<object> _items = [];
    public override int Count => _items.Count;
    public override object SyncRoot { get; } = new();
    public override int Add(object value) { _items.Add(value); return _items.Count - 1; }
    public override void AddRange(Array values) => _items.AddRange(values.Cast<object>());
    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains(value);
    public override bool Contains(string value) => false;
    public override void CopyTo(Array array, int index) => _items.ToArray().CopyTo(array, index);
    public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
    public override int IndexOf(object value) => _items.IndexOf(value);
    public override int IndexOf(string parameterName) => -1;
    public override void Insert(int index, object value) => _items.Insert(index, value);
    public override void Remove(object value) => _items.Remove(value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName) { }
    protected override DbParameter GetParameter(int index) => (DbParameter)_items[index];
    protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) => throw new NotSupportedException();
}

/// <summary>Fake command that returns a fixed scalar value for SELECT 1.</summary>
file sealed class FakeDbCommand : DbCommand
{
    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    public override int ExecuteNonQuery() => 0;
    public override object ExecuteScalar() => 1;
    public override void Prepare() { }
}

/// <summary>Fake connection that opens successfully and tracks disposal.</summary>
file sealed class FakeDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    public new bool Disposed { get; private set; }

    [AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "fake";
    public override string DataSource => "fake";
    public override string ServerVersion => "1.0";
    public override ConnectionState State => _state;

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => _state = ConnectionState.Closed;
    protected override DbCommand CreateDbCommand() => new FakeDbCommand { Connection = this };
    public override void Open() => _state = ConnectionState.Open;

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        _state = ConnectionState.Closed;
        base.Dispose(disposing);
    }
}

/// <summary>Connection factory that always returns a healthy open connection.</summary>
file sealed class HealthyConnectionFactory : IDbConnectionFactory
{
    public FakeDbConnection? LastConnection { get; private set; }

    public Task<DbConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        var connection = new FakeDbConnection();
        connection.Open();
        LastConnection = connection;
        return Task.FromResult<DbConnection>(connection);
    }
}

/// <summary>Connection factory that always throws when a connection is requested.</summary>
file sealed class ThrowingConnectionFactory : IDbConnectionFactory
{
    public Task<DbConnection> CreateConnectionAsync(CancellationToken ct = default)
        => throw new InvalidOperationException("Connection refused.");
}

// ---------------------------------------------------------------------------
// Genuine-async proof fakes. Every synchronous override THROWS — if the probe ever
// regresses to calling the synchronous ExecuteScalar(), the test fails loudly instead of silently
// passing.
// ---------------------------------------------------------------------------

file sealed class AsyncOnlyParameterCollection : DbParameterCollection
{
    private readonly List<object> _items = [];
    public override int Count => _items.Count;
    public override object SyncRoot { get; } = new();
    public override int Add(object value) { _items.Add(value); return _items.Count - 1; }
    public override void AddRange(Array values) => _items.AddRange(values.Cast<object>());
    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains(value);
    public override bool Contains(string value) => false;
    public override void CopyTo(Array array, int index) => _items.ToArray().CopyTo(array, index);
    public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
    public override int IndexOf(object value) => _items.IndexOf(value);
    public override int IndexOf(string parameterName) => -1;
    public override void Insert(int index, object value) => _items.Insert(index, value);
    public override void Remove(object value) => _items.Remove(value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName) { }
    protected override DbParameter GetParameter(int index) => (DbParameter)_items[index];
    protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) => throw new NotSupportedException();
}

/// <summary>
/// Genuine <see cref="DbCommand"/> fake whose SYNCHRONOUS overrides throw and whose ASYNC overrides
/// succeed and record invocation.
/// </summary>
file sealed class AsyncOnlyDbCommand : DbCommand
{
    public bool AsyncScalarCalled { get; private set; }

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new AsyncOnlyParameterCollection();
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();

    public override int ExecuteNonQuery() =>
        throw new InvalidOperationException("Synchronous ExecuteNonQuery must not be called — the async overload must be used.");

    public override object ExecuteScalar() =>
        throw new InvalidOperationException("Synchronous ExecuteScalar must not be called — the async overload must be used.");

    public override void Prepare() { }

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        AsyncScalarCalled = true;
        return Task.FromResult<object?>(1);
    }
}

/// <summary>
/// Genuine <see cref="DbConnection"/> fake backing <see cref="AsyncOnlyDbCommand"/>.
/// </summary>
file sealed class AsyncOnlyDbConnection : DbConnection
{
    public AsyncOnlyDbCommand? LastCommand { get; private set; }

    [AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "fake-async-only";
    public override string DataSource => "fake";
    public override string ServerVersion => "1.0";
    public override ConnectionState State { get; } = ConnectionState.Open;

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException();
    protected override DbCommand CreateDbCommand()
    {
        var command = new AsyncOnlyDbCommand();
        LastCommand = command;
        return command;
    }
}

file sealed class AsyncOnlyConnectionFactory : IDbConnectionFactory
{
    public AsyncOnlyDbConnection? LastConnection { get; private set; }

    public Task<DbConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        var connection = new AsyncOnlyDbConnection();
        LastConnection = connection;
        return Task.FromResult<DbConnection>(connection);
    }
}

// ---------------------------------------------------------------------------
// IDbConnectionFactory.CheckReadinessAsync behavioral tests (C-89)
// ---------------------------------------------------------------------------

public sealed class DatabaseReadinessTests
{
    [Fact]
    public async Task CheckReadinessAsync_GenuineDbCommand_InvokesAsyncOverload_NotSynchronous()
    {
        // Proves the async overload is genuinely invoked: AsyncOnlyDbCommand's
        // synchronous ExecuteScalar() throws, so this test would fail loudly if the fix regressed.
        var factory = new AsyncOnlyConnectionFactory();

        var result = await factory.CheckReadinessAsync();

        result.IsHealthy.Should().BeTrue();
        factory.LastConnection.Should().NotBeNull();
        factory.LastConnection!.LastCommand.Should().NotBeNull();
        factory.LastConnection.LastCommand!.AsyncScalarCalled.Should().BeTrue(
            "CheckReadinessAsync must call ExecuteScalarAsync, not the synchronous ExecuteScalar()");
    }


    [Fact]
    public async Task CheckReadinessAsync_HealthyConnection_ReturnsIsHealthyTrue()
    {
        var factory = new HealthyConnectionFactory();

        var result = await factory.CheckReadinessAsync();

        result.IsHealthy.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.Provider.Should().Contain(nameof(FakeDbConnection));
        result.Latency.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task CheckReadinessAsync_HealthyConnection_DisposesConnection()
    {
        var factory = new HealthyConnectionFactory();

        await factory.CheckReadinessAsync();

        factory.LastConnection.Should().NotBeNull();
        factory.LastConnection!.Disposed.Should().BeTrue("CheckReadinessAsync must dispose the connection it opens");
    }

    [Fact]
    public async Task CheckReadinessAsync_ThrowingFactory_ReturnsIsHealthyFalse_WithoutThrowing()
    {
        var factory = new ThrowingConnectionFactory();

        var act = async () => await factory.CheckReadinessAsync();

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsHealthy.Should().BeFalse();
        // ErrorMessage never carries Exception.Message (a driver-level failure message can
        // embed the connection string/host/credentials) — only the exception's CLR type name.
        result.Subject.ErrorMessage.Should().Be(nameof(InvalidOperationException));
        result.Subject.Provider.Should().Be("unknown");
    }

    [Fact]
    public async Task CheckReadinessAsync_TimeoutExceeded_ReturnsIsHealthyFalse_WithTimeoutMessage()
    {
        // A hung connection attempt must not block the probe indefinitely.
        var factory = new HangingConnectionFactory();

        var result = await factory.CheckReadinessAsync(timeout: TimeSpan.FromMilliseconds(50));

        result.IsHealthy.Should().BeFalse();
        result.ErrorMessage.Should().Be("Timeout");
    }

    [Fact]
    public async Task CheckReadinessAsync_CallerCancellation_PropagatesOperationCanceledException()
    {
        // The caller's own cancellation (shutdown/deadline) must propagate, never be
        // reported as a fabricated unhealthy result.
        var factory = new HangingConnectionFactory();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await factory.CheckReadinessAsync(cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

/// <summary>Connection factory whose connection never opens — used to prove the timeout guard.</summary>
file sealed class HangingConnectionFactory : IDbConnectionFactory
{
    public async Task<DbConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new UnreachableException();
    }
}
