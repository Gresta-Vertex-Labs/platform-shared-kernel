using System.Data;
using System.Data.Common;
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

    private static (IDbConnectionFactory Factory, RecordingDbCommand Command, RecordingParameter Parameter) BuildConnectionFactory(
        StubDataReader reader)
    {
        var parameter = new RecordingParameter();
        var command = new RecordingDbCommand(reader, parameter);
        var connection = new RecordingDbConnection(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<DbConnection>(connection));

        return (factory, command, parameter);
    }

    /// <summary>Minimal genuine <see cref="DbDataReader"/> double — every
    /// <see cref="IDbConnectionFactory"/> implementer now returns a real <see cref="DbConnection"/>, so
    /// <see cref="DatabaseTenantCatalog"/>'s command/reader are always genuine
    /// <see cref="DbCommand"/>/<see cref="DbDataReader"/> instances — the former bare-<see cref="IDataReader"/>
    /// synchronous-fallback scenario this double exercised no longer exists as a reachable code path.</summary>
    private sealed class StubDataReader(
        bool hasRow,
        Guid tenantId = default,
        string displayName = "",
        string status = "Active",
        string isolationMode = "Shared",
        string? defaultCulture = null) : DbDataReader
    {
        private bool _consumed;

        public override int FieldCount => 5;

        public override bool Read()
        {
            if (_consumed || !hasRow)
            {
                return false;
            }

            _consumed = true;
            return true;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

        public override Guid GetGuid(int ordinal) => tenantId;

        public override string GetString(int ordinal) => ordinal switch
        {
            1 => displayName,
            2 => status,
            3 => isolationMode,
            4 => defaultCulture ?? string.Empty,
            _ => string.Empty,
        };

        public override bool IsDBNull(int ordinal) => ordinal == 4 && defaultCulture is null;

        public override int Depth => 0;

        public override bool HasRows => hasRow;

        public override bool IsClosed => false;

        public override int RecordsAffected => 0;

        public override bool NextResult() => false;

        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();

        public override byte GetByte(int ordinal) => throw new NotSupportedException();

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
            throw new NotSupportedException();

        public override char GetChar(int ordinal) => throw new NotSupportedException();

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
            throw new NotSupportedException();

        public override string GetDataTypeName(int ordinal) => throw new NotSupportedException();

        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();

        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();

        public override double GetDouble(int ordinal) => throw new NotSupportedException();

        public override Type GetFieldType(int ordinal) => throw new NotSupportedException();

        public override float GetFloat(int ordinal) => throw new NotSupportedException();

        public override short GetInt16(int ordinal) => throw new NotSupportedException();

        public override int GetInt32(int ordinal) => throw new NotSupportedException();

        public override long GetInt64(int ordinal) => throw new NotSupportedException();

        public override string GetName(int ordinal) => throw new NotSupportedException();

        public override int GetOrdinal(string name) => throw new NotSupportedException();

        public override object GetValue(int ordinal) => throw new NotSupportedException();

        public override int GetValues(object[] values) => throw new NotSupportedException();

        public override object this[int ordinal] => throw new NotSupportedException();

        public override object this[string name] => throw new NotSupportedException();

        public override System.Collections.IEnumerator GetEnumerator() => throw new NotSupportedException();
    }

    /// <summary>Minimal <see cref="DbParameter"/> test double recording its bound <see cref="Value"/>.</summary>
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

    /// <summary>Minimal genuine <see cref="DbCommand"/> test double, backed by a caller-supplied
    /// <see cref="StubDataReader"/> and a single, always-reused <see cref="RecordingParameter"/>.</summary>
    private sealed class RecordingDbCommand(StubDataReader reader, RecordingParameter parameter) : DbCommand
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
        protected override DbParameter CreateDbParameter() => parameter;
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => reader;

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
            => Task.FromResult<DbDataReader>(reader);
    }

    /// <summary>Minimal genuine <see cref="DbConnection"/> test double returning a pre-built
    /// <see cref="RecordingDbCommand"/>.</summary>
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
