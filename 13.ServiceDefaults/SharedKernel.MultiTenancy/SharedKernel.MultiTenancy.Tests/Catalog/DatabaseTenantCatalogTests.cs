using System.Data;
using NSubstitute;
using SharedKernel.MultiTenancy.Catalog;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.MultiTenancy.Tests.Catalog;

public sealed class DatabaseTenantCatalogTests
{
    [Fact]
    public async Task GetByIdAsync_WithKnownTenant_ReturnsExpectedDescriptor()
    {
        var tenantId = Guid.NewGuid();
        var (factory, _, _) = BuildConnectionFactory(
            new StubDataReader(hasRow: true, tenantId, "Acme", "Active", "Shared", null));

        var catalog = new DatabaseTenantCatalog(factory);

        var result = await catalog.GetByIdAsync(tenantId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(tenantId, result!.TenantId);
        Assert.Equal("Acme", result.DisplayName);
        Assert.Equal(TenantStatus.Active, result.Status);
        Assert.Equal(TenantIsolationMode.Shared, result.IsolationMode);
        Assert.Null(result.DefaultCulture);
        Assert.Empty(result.Settings);
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownTenant_ReturnsNull()
    {
        var (factory, _, _) = BuildConnectionFactory(new StubDataReader(hasRow: false));

        var catalog = new DatabaseTenantCatalog(factory);

        var result = await catalog.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_WithKnownKey_ReturnsExpectedDescriptor()
    {
        var tenantId = Guid.NewGuid();
        var (factory, _, _) = BuildConnectionFactory(
            new StubDataReader(hasRow: true, tenantId, "Acme", "Suspended", "Dedicated", "tr-TR"));

        var catalog = new DatabaseTenantCatalog(factory);

        var result = await catalog.GetByResolutionKeyAsync("acme.api.example.com", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(TenantStatus.Suspended, result!.Status);
        Assert.Equal(TenantIsolationMode.Dedicated, result.IsolationMode);
        Assert.Equal("tr-TR", result.DefaultCulture);
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_WithUnknownKey_ReturnsNull()
    {
        var (factory, _, _) = BuildConnectionFactory(new StubDataReader(hasRow: false));

        var catalog = new DatabaseTenantCatalog(factory);

        var result = await catalog.GetByResolutionKeyAsync("unknown.example.com", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_UsesParameterizedQuery_NoStringBuiltSql()
    {
        var (factory, command, parameter) = BuildConnectionFactory(new StubDataReader(hasRow: false));

        var catalog = new DatabaseTenantCatalog(factory);

        await catalog.GetByResolutionKeyAsync("acme.api.example.com", CancellationToken.None);

        // The command text must not contain the request-derived resolution-key value — it must be
        // passed exclusively via a bound parameter.
        Assert.DoesNotContain("acme.api.example.com", command.CommandText);
        Assert.Equal("acme.api.example.com", parameter.Value);
    }

    private static (IDbConnectionFactory Factory, IDbCommand Command, IDbDataParameter Parameter) BuildConnectionFactory(
        StubDataReader reader)
    {
        var command = Substitute.For<IDbCommand>();
        var parameters = Substitute.For<IDataParameterCollection>();
        var parameter = Substitute.For<IDbDataParameter>();
        command.Parameters.Returns(parameters);
        command.CreateParameter().Returns(parameter);
        command.ExecuteReader().Returns(reader);

        var connection = Substitute.For<IDbConnection>();
        connection.CreateCommand().Returns(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(connection));

        return (factory, command, parameter);
    }

    /// <summary>Minimal, non-<see cref="System.Data.Common.DbDataReader"/> <see cref="IDataReader"/>
    /// double exercising the catalog's synchronous fallback path (the command double is a bare
    /// <see cref="IDbCommand"/>, not a <see cref="System.Data.Common.DbCommand"/>).</summary>
    private sealed class StubDataReader(
        bool hasRow,
        Guid tenantId = default,
        string displayName = "",
        string status = "Active",
        string isolationMode = "Shared",
        string? defaultCulture = null) : IDataReader
    {
        private bool _consumed;

        public int FieldCount => 5;

        public bool Read()
        {
            if (_consumed || !hasRow)
            {
                return false;
            }

            _consumed = true;
            return true;
        }

        public Guid GetGuid(int i) => tenantId;

        public string GetString(int i) => i switch
        {
            1 => displayName,
            2 => status,
            3 => isolationMode,
            4 => defaultCulture ?? string.Empty,
            _ => string.Empty,
        };

        public bool IsDBNull(int i) => i == 4 && defaultCulture is null;

        public void Dispose()
        {
        }

        public void Close()
        {
        }

        public int Depth => 0;

        public DataTable? GetSchemaTable() => null;

        public bool IsClosed => false;

        public bool NextResult() => false;

        public int RecordsAffected => 0;

        public bool GetBoolean(int i) => throw new NotSupportedException();

        public byte GetByte(int i) => throw new NotSupportedException();

        public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) =>
            throw new NotSupportedException();

        public char GetChar(int i) => throw new NotSupportedException();

        public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) =>
            throw new NotSupportedException();

        public IDataReader GetData(int i) => throw new NotSupportedException();

        public string GetDataTypeName(int i) => throw new NotSupportedException();

        public DateTime GetDateTime(int i) => throw new NotSupportedException();

        public decimal GetDecimal(int i) => throw new NotSupportedException();

        public double GetDouble(int i) => throw new NotSupportedException();

        public Type GetFieldType(int i) => throw new NotSupportedException();

        public float GetFloat(int i) => throw new NotSupportedException();

        public short GetInt16(int i) => throw new NotSupportedException();

        public int GetInt32(int i) => throw new NotSupportedException();

        public long GetInt64(int i) => throw new NotSupportedException();

        public string GetName(int i) => throw new NotSupportedException();

        public int GetOrdinal(string name) => throw new NotSupportedException();

        public object GetValue(int i) => throw new NotSupportedException();

        public int GetValues(object[] values) => throw new NotSupportedException();

        public object this[int i] => throw new NotSupportedException();

        public object this[string name] => throw new NotSupportedException();
    }
}
