using System.Data;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Diagnostics;

// CS8767: ADO.NET's IDbConnection/IDbCommand interfaces have inconsistent nullable annotations
// between getters and setters on ConnectionString/CommandText across target frameworks. The fakes
// below intentionally use non-nullable string properties for simplicity.
#pragma warning disable CS8767

namespace SharedKernel.Persistence.Abstractions.Tests.Diagnostics;

// ---------------------------------------------------------------------------
// Minimal in-memory IDbConnection / IDbCommand fakes (C-89)
// ---------------------------------------------------------------------------

/// <summary>Fake command that returns a fixed scalar value for SELECT 1.</summary>
file sealed class FakeDbCommand : IDbCommand
{
    public string CommandText { get; set; } = string.Empty;
    public int CommandTimeout { get; set; }
    public CommandType CommandType { get; set; }
    public IDbConnection? Connection { get; set; }
    public IDataParameterCollection Parameters { get; } = new FakeParameterCollection();
    public IDbTransaction? Transaction { get; set; }
    public UpdateRowSource UpdatedRowSource { get; set; }

    public void Cancel() { }
    public IDbDataParameter CreateParameter() => throw new NotSupportedException();
    public void Dispose() { }
    public int ExecuteNonQuery() => 0;
    public IDataReader ExecuteReader() => throw new NotSupportedException();
    public IDataReader ExecuteReader(CommandBehavior behavior) => throw new NotSupportedException();
    public object ExecuteScalar() => 1;
    public void Prepare() { }
}

file sealed class FakeParameterCollection : List<object>, IDataParameterCollection
{
    public bool Contains(string parameterName) => false;
    public int IndexOf(string parameterName) => -1;
    public void RemoveAt(string parameterName) { }
    public object this[string parameterName]
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
}

/// <summary>Fake connection that opens successfully and tracks disposal.</summary>
file sealed class FakeDbConnection : IDbConnection
{
    public bool Disposed { get; private set; }

    public string ConnectionString { get; set; } = string.Empty;
    public int ConnectionTimeout => 0;
    public string Database => "fake";
    public ConnectionState State { get; private set; } = ConnectionState.Closed;

    public IDbTransaction BeginTransaction() => throw new NotSupportedException();
    public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
    public void ChangeDatabase(string databaseName) { }
    public void Close() => State = ConnectionState.Closed;
    public IDbCommand CreateCommand() => new FakeDbCommand { Connection = this };

    public void Dispose()
    {
        Disposed = true;
        State = ConnectionState.Closed;
    }

    public void Open() => State = ConnectionState.Open;
}

/// <summary>Connection factory that always returns a healthy open connection.</summary>
file sealed class HealthyConnectionFactory : IDbConnectionFactory
{
    public FakeDbConnection? LastConnection { get; private set; }

    public Task<IDbConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        var connection = new FakeDbConnection();
        connection.Open();
        LastConnection = connection;
        return Task.FromResult<IDbConnection>(connection);
    }
}

/// <summary>Connection factory that always throws when a connection is requested.</summary>
file sealed class ThrowingConnectionFactory : IDbConnectionFactory
{
    public Task<IDbConnection> CreateConnectionAsync(CancellationToken ct = default)
        => throw new InvalidOperationException("Connection refused.");
}

// ---------------------------------------------------------------------------
// WO-051/P-325 — genuine-async proof fakes. Unlike FakeDbCommand/FakeDbConnection above (plain
// IDbCommand/IDbConnection, which cannot prove which overload was actually invoked), these derive
// from the real System.Data.Common.DbCommand/DbConnection abstract base classes so the
// DbConnectionFactoryDiagnosticsExtensions.CheckReadinessAsync `command is DbCommand` safe-cast
// takes the TRUE branch. Every synchronous override THROWS — if the fix ever regresses to calling
// the synchronous ExecuteScalar(), the test fails loudly instead of silently passing.
// ---------------------------------------------------------------------------

file sealed class AsyncOnlyParameterCollection : System.Data.Common.DbParameterCollection
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
    protected override System.Data.Common.DbParameter GetParameter(int index) => (System.Data.Common.DbParameter)_items[index];
    protected override System.Data.Common.DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
    protected override void SetParameter(int index, System.Data.Common.DbParameter value) => _items[index] = value;
    protected override void SetParameter(string parameterName, System.Data.Common.DbParameter value) => throw new NotSupportedException();
}

/// <summary>
/// Genuine <see cref="System.Data.Common.DbCommand"/> fake whose SYNCHRONOUS overrides throw and
/// whose ASYNC overrides succeed and record invocation (WO-051/P-325).
/// </summary>
file sealed class AsyncOnlyDbCommand : System.Data.Common.DbCommand
{
    public bool AsyncScalarCalled { get; private set; }

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override System.Data.Common.DbConnection? DbConnection { get; set; }
    protected override System.Data.Common.DbParameterCollection DbParameterCollection { get; } = new AsyncOnlyParameterCollection();
    protected override System.Data.Common.DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    protected override System.Data.Common.DbParameter CreateDbParameter() => throw new NotSupportedException();
    protected override System.Data.Common.DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();

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
/// Genuine <see cref="System.Data.Common.DbConnection"/> fake backing <see cref="AsyncOnlyDbCommand"/>.
/// </summary>
file sealed class AsyncOnlyDbConnection : System.Data.Common.DbConnection
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
    protected override System.Data.Common.DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        throw new NotSupportedException();
    protected override System.Data.Common.DbCommand CreateDbCommand()
    {
        var command = new AsyncOnlyDbCommand();
        LastCommand = command;
        return command;
    }
}

file sealed class AsyncOnlyConnectionFactory : IDbConnectionFactory
{
    public AsyncOnlyDbConnection? LastConnection { get; private set; }

    public Task<IDbConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        var connection = new AsyncOnlyDbConnection();
        LastConnection = connection;
        return Task.FromResult<IDbConnection>(connection);
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
        // WO-051/P-325 — proves the async overload is genuinely invoked: AsyncOnlyDbCommand's
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
        result.Subject.ErrorMessage.Should().Be("Connection refused.");
        result.Subject.Provider.Should().Be("unknown");
    }
}
