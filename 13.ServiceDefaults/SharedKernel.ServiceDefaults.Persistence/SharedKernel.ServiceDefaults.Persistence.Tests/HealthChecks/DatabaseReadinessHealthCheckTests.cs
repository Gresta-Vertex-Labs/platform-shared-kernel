using System.Data;
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.ServiceDefaults.Persistence.Tests.HealthChecks;

public sealed class DatabaseReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_HealthyDatabase_ReportsHealthy_WithLatencyAndProviderData()
    {
        using var context = CreateSqliteContext();
        var healthCheck = new DatabaseReadinessHealthCheck<TestDbContext>(context);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("Latency");
        result.Data.Should().ContainKey("Provider");
    }

    [Fact]
    public async Task CheckHealthAsync_UnreachableDatabase_ReportsUnhealthy_WithLatencyAndProviderData()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("Data Source=/nonexistent/path/that/cannot/be/created.db")
                .Options;
        using var context = new TestDbContext(
            options,
            new PersistenceContextDependencies(
                new AuditInterceptor(new FakeAuditActorContext(), new FakeClock()),
            new SoftDeleteInterceptor(new FakeAuditActorContext(), new FakeClock()),
            new ConcurrencyInterceptor()));

        var healthCheck = new DatabaseReadinessHealthCheck<TestDbContext>(context);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data.Should().ContainKey("Latency");
        result.Data.Should().ContainKey("Provider");
    }

    private static TestDbContext CreateSqliteContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
                .Options;

        var context = new TestDbContext(
            options,
            new PersistenceContextDependencies(
                new AuditInterceptor(new FakeAuditActorContext(), new FakeClock()),
            new SoftDeleteInterceptor(new FakeAuditActorContext(), new FakeClock()),
            new ConcurrencyInterceptor()));

        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>Minimal <see cref="SharedKernelDbContext"/> subclass with no entities, used only
    /// to exercise <see cref="DatabaseReadinessHealthCheck{TContext}"/> against a real SQLite
    /// connection.</summary>
    internal sealed class TestDbContext(
        DbContextOptions options,
        PersistenceContextDependencies dependencies)
            : SharedKernelDbContext(options, dependencies);
}

public sealed class DapperDatabaseReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_HealthyConnectionFactory_ReportsHealthy_WithLatencyAndProviderData()
    {
        var command = new RecordingDbCommand(scalarResult: 1);
        var connection = new RecordingDbConnection(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<DbConnection>(connection));

        var healthCheck = new DapperDatabaseReadinessHealthCheck(factory);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("Latency");
        result.Data.Should().ContainKey("Provider");
    }

    [Fact]
    public async Task CheckHealthAsync_FactoryThrows_ReportsUnhealthy_WithLatencyAndProviderData()
    {
        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DbConnection>(new InvalidOperationException("connection refused")));

        var healthCheck = new DapperDatabaseReadinessHealthCheck(factory);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data.Should().ContainKey("Latency");
        result.Data.Should().ContainKey("Provider");
    }

    /// <summary>Minimal genuine <see cref="DbCommand"/> test double —
    /// <see cref="IDbConnectionFactory"/> now returns a real <see cref="DbConnection"/>, so NSubstitute
    /// can no longer mock <see cref="IDbCommand"/>/<see cref="IDbConnection"/> for this path.</summary>
    private sealed class RecordingDbCommand(object? scalarResult) : DbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new RecordingParameterCollection();
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        protected override DbParameter CreateDbParameter() => new RecordingParameter();
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => scalarResult;
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
            Task.FromResult(scalarResult);
        public override void Prepare() { }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        public override int Size { get; set; }
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }

        public override void ResetDbType() { }
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

        public override void AddRange(Array values) => _items.AddRange(values.Cast<object>());
        public override void Clear() => _items.Clear();
        public override bool Contains(string value) => false;
        public override bool Contains(object value) => _items.Contains(value);
        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(string parameterName) => -1;
        public override int IndexOf(object value) => _items.IndexOf(value);
        public override void Insert(int index, object value) => _items.Insert(index, value);
        public override void Remove(object value) => _items.Remove(value);
        public override void RemoveAt(string parameterName) { }
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
        protected override DbParameter GetParameter(int index) => (DbParameter)_items[index];
        protected override void SetParameter(string parameterName, DbParameter value) { }
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    }

    private sealed class RecordingDbConnection(RecordingDbCommand command) : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbCommand CreateDbCommand() => command;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();
    }
}
