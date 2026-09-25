using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.MultiTenancy.Tests.Resolution;

public sealed class DatabaseTenantResolutionStrategyTests
{
    [Fact]
    public async Task TryResolveAsync_WithKnownHost_ReturnsExpectedTenantId()
    {
        var tenantId = Guid.NewGuid();
        var command = new RecordingDbCommand(tenantId.ToString());
        var connection = new RecordingDbConnection(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<DbConnection>(connection));

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("acme.api.example.com");

        var strategy = new DatabaseTenantResolutionStrategy(factory);

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Equal(tenantId, result?.Value);
    }

    [Fact]
    public async Task TryResolveAsync_WithUnknownHost_ReturnsNull()
    {
        var command = new RecordingDbCommand(null);
        var connection = new RecordingDbConnection(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<DbConnection>(connection));

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("unknown.example.com");

        var strategy = new DatabaseTenantResolutionStrategy(factory);

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_UsesParameterizedQuery_NoStringBuiltSql()
    {
        var command = new RecordingDbCommand(null);
        var connection = new RecordingDbConnection(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<DbConnection>(connection));

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("acme.api.example.com");

        var strategy = new DatabaseTenantResolutionStrategy(factory);

        await strategy.TryResolveAsync(context, CancellationToken.None);

        // The command text must not contain the request-derived host value — it must be passed
        // exclusively via a bound parameter.
        Assert.DoesNotContain("acme.api.example.com", command.CommandText);
        Assert.Equal(1, command.Parameters.Count);
        Assert.Equal("acme.api.example.com", ((DbParameter)command.Parameters[0]!).Value);
    }

    [Fact]
    public async Task TryResolveAsync_WithDbCommand_UsesAsyncExecuteScalarPath_NotSynchronousExecuteScalar()
    {
        var tenantId = Guid.NewGuid();
        var command = new RecordingDbCommand(tenantId.ToString());
        var connection = new RecordingDbConnection(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<DbConnection>(connection));

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("acme.api.example.com");

        var strategy = new DatabaseTenantResolutionStrategy(factory);

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Equal(tenantId, result?.Value);
        Assert.True(command.AsyncPathInvoked);
        Assert.False(command.SyncPathInvoked);
    }

    [Fact]
    public async Task TryResolveAsync_WithCancelledToken_PropagatesCancellation()
    {
        var command = new RecordingDbCommand(null) { ThrowOnCancellation = true };
        var connection = new RecordingDbConnection(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<DbConnection>(connection));

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("acme.api.example.com");

        var strategy = new DatabaseTenantResolutionStrategy(factory);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => strategy.TryResolveAsync(context, cts.Token));
    }

    /// <summary>Minimal <see cref="DbCommand"/> test double recording whether the async or
    /// synchronous <c>ExecuteScalar</c> path was invoked, and optionally honoring cancellation.</summary>
    private sealed class RecordingDbCommand(string? scalarResult) : DbCommand
    {
        public bool AsyncPathInvoked { get; private set; }

        public bool SyncPathInvoked { get; private set; }

        public bool ThrowOnCancellation { get; set; }

        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection { get; } = new RecordingParameterCollection();

        protected override DbTransaction? DbTransaction { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => 0;

        public override object? ExecuteScalar()
        {
            SyncPathInvoked = true;
            return scalarResult;
        }

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            if (ThrowOnCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            AsyncPathInvoked = true;
            return Task.FromResult<object?>(scalarResult);
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new RecordingParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        public override string? ParameterName { get; set; } = string.Empty;

        public override int Size { get; set; }

        public override string? SourceColumn { get; set; } = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType()
        {
        }
    }

    private sealed class RecordingParameterCollection : DbParameterCollection
    {
        private readonly List<object> _items = [];

        public override int Count => _items.Count;

        public override object SyncRoot { get; } = new();

        public override int Add(object value)
        {
            _items.Add(value);
            return _items.Count - 1;
        }

        public override void AddRange(System.Array values) => _items.AddRange(values.Cast<object>());

        public override void Clear() => _items.Clear();

        public override bool Contains(string value) => false;

        public override bool Contains(object value) => _items.Contains(value);

        public override void CopyTo(System.Array array, int index) =>
            ((System.Collections.ICollection)_items).CopyTo(array, index);

        public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();

        public override int IndexOf(string parameterName) => -1;

        public override int IndexOf(object value) => _items.IndexOf(value);

        public override void Insert(int index, object value) => _items.Insert(index, value);

        public override void Remove(object value) => _items.Remove(value);

        public override void RemoveAt(string parameterName)
        {
        }

        public override void RemoveAt(int index) => _items.RemoveAt(index);

        protected override DbParameter GetParameter(string parameterName) =>
            throw new NotSupportedException();

        protected override DbParameter GetParameter(int index) => (DbParameter)_items[index];

        protected override void SetParameter(string parameterName, DbParameter value)
        {
        }

        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    }

    /// <summary>Minimal <see cref="DbConnection"/> test double returning a pre-built <see cref="RecordingDbCommand"/>.</summary>
    private sealed class RecordingDbConnection(RecordingDbCommand command) : DbConnection
    {
        public override string? ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbCommand CreateDbCommand() => command;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();
    }
}
