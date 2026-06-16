using System.Data;
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
// IDbConnectionFactory.CheckReadinessAsync behavioral tests (C-89)
// ---------------------------------------------------------------------------

public sealed class DatabaseReadinessTests
{
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
