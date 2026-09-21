using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Persistence.Npgsql.Diagnostics;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Persistence.Npgsql.Options;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Npgsql.Tests.Extensions;

/// <summary>
/// One configuration shape (finding F9): a named database reads <c>ConnectionStrings:{name}</c> and
/// <c>SharedKernel:Persistence:{name}</c>, exactly like <c>AddSharedKernelPostgres(name)</c>.
/// </summary>
public sealed class NpgsqlConfigurationShapeTests
{
    private static NpgsqlPersistenceOptions Bind(Dictionary<string, string?> settings, Action<IServiceCollection, IConfiguration> register)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        register(services, configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<NpgsqlPersistenceOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);
    }

    [Fact]
    public void NamedDatabase_ReadsItsOwnSection_AndConnectionStrings()
    {
        var options = Bind(new()
        {
            ["ConnectionStrings:orders"] = "Host=localhost;Database=orders",
            ["SharedKernel:Persistence:orders:StatementTimeoutMilliseconds"] = "1500",
            ["SharedKernel:Persistence:orders:RowLevelSecurity:Enabled"] = "true",
            ["SharedKernel:Persistence:Npgsql:LockTimeoutMilliseconds"] = "999",
        }, (s, c) => s.AddSharedKernelNpgsql(c, "orders"));

        options.ConnectionString.Should().Be("Host=localhost;Database=orders");
        options.StatementTimeoutMilliseconds.Should().Be(1500);
        options.RowLevelSecurity.Enabled.Should().BeTrue();
        options.LockTimeoutMilliseconds.Should().BeNull("a named database never reads the unnamed registration's section");
        options.SectionPath.Should().Be("SharedKernel:Persistence:orders");
    }

    [Fact]
    public void NamedDatabase_ConnectionStringInItsSection_OverridesConnectionStrings()
    {
        var options = Bind(new()
        {
            ["ConnectionStrings:orders"] = "Host=from-connection-strings",
            ["SharedKernel:Persistence:orders:ConnectionString"] = "Host=from-section",
        }, (s, c) => s.AddSharedKernelNpgsql(c, "orders"));

        options.ConnectionString.Should().Be("Host=from-section");
    }

    [Fact]
    public void UnnamedRegistration_ReadsTheNpgsqlSection()
    {
        var options = Bind(new()
        {
            ["SharedKernel:Persistence:Npgsql:ConnectionString"] = "Host=localhost",
            ["SharedKernel:Persistence:Npgsql:LockTimeoutMilliseconds"] = "999",
        }, (s, c) => s.AddSharedKernelNpgsql(c));

        options.LockTimeoutMilliseconds.Should().Be(999);
        options.SectionPath.Should().Be(NpgsqlPersistenceOptions.SectionName);
    }

    [Fact]
    public void MissingConnectionString_NamesBothPlacesItIsReadFrom()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelNpgsql(configuration, "orders");
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptionsMonitor<NpgsqlPersistenceOptions>>().Get(Microsoft.Extensions.Options.Options.DefaultName);

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ConnectionStrings:orders*SharedKernel:Persistence:orders:ConnectionString*");
    }
}

/// <summary>Npgsql's per-command log is written at Debug, not Information (finding F16).</summary>
public sealed class NpgsqlCommandLoggingTests(PostgreSqlContainerFixture fixture) : IClassFixture<PostgreSqlContainerFixture>
{
    [Fact]
    public async Task ExecutedCommands_AreNotLoggedAtInformation_ButStillAtDebug()
    {
        var recorder = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(recorder));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:orders"] = fixture.ConnectionString + ";SSL Mode=Disable",
        }).Build();
        services.AddSharedKernelNpgsql(configuration, "orders");
        await using var provider = services.BuildServiceProvider();

        await using (var command = provider.GetRequiredService<NpgsqlDataSource>().CreateCommand("SELECT 1"))
            await command.ExecuteScalarAsync();

        var commandLogs = recorder.Entries.Where(e => e.Category == NpgsqlCommandLogLevel.CommandCategory).ToList();
        commandLogs.Should().NotBeEmpty("Npgsql logs executed commands");
        commandLogs.Should().OnlyContain(e => e.Level <= LogLevel.Debug);
        commandLogs.Should().Contain(e => e.Message.Contains("SELECT 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NpgsqlItself_LogsExecutedCommandsAtInformation_WhichIsWhyTheyAreDowngraded()
    {
        var recorder = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(recorder));
        var builder = new NpgsqlDataSourceBuilder(fixture.ConnectionString);
        builder.UseLoggerFactory(loggerFactory);
        await using var dataSource = builder.Build();

        await using (var command = dataSource.CreateCommand("SELECT 1"))
            await command.ExecuteScalarAsync();

        recorder.Entries.Should().Contain(e => e.Category == NpgsqlCommandLogLevel.CommandCategory && e.Level == LogLevel.Information);
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<(string Category, LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Recorder(RecordingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Entries)
                    owner.Entries.Add((category, logLevel, formatter(state, exception)));
            }
        }
    }
}
