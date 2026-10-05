using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Persistence.EfCore.Auditing.SelfCheck;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>
/// F1 (wave 3b, stream R1): the startup self-check runs only after the startup migrations completed, so a fresh
/// deployment that creates the ledger with <c>MigrateOnStartup()</c> is never judged against a missing schema.
/// </summary>
[Collection("AuditPostgres")]
public sealed class StartupOrderingTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task SelfCheck_WaitsForTheStartupMigrations_ThenChecksTheMigratedSchema()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture, withLedger: false);
        var startup = new PersistenceStartupSignal();
        startup.Expect(typeof(StartupOrderingTests));
        var logger = new CollectingLogger<AuditLedgerSelfCheckHostedService>();

        var service = new AuditLedgerSelfCheckHostedService(
            new AuditLedgerSelfCheck(new TestConnectionFactory(cs)),
            Microsoft.Extensions.Options.Options.Create(new AuditLedgerOptions { SelfCheck = AuditSelfCheckMode.Warn }),
            logger,
            startup);

        await service.StartAsync(CancellationToken.None); // returns at once: nothing happens before every service started
        var started = service.StartedAsync(CancellationToken.None);
        await Task.Delay(200);
        started.IsCompleted.Should().BeFalse("the self-check waits for the startup migrations");

        // The "migration" creates the ledger, then startup completes.
        await LedgerTestDatabase.ExecuteAsync(cs, AuditLedgerSchema.CreateScript);
        startup.Complete(typeof(StartupOrderingTests));
        await started;

        logger.Messages.Should().NotContain(m => m.Contains("does not exist", StringComparison.Ordinal),
            "the check ran against the migrated schema");
    }

    [Fact]
    public async Task SelfCheck_DoesNotRun_WhenTheStartupMigrationsFailed()
    {
        var startup = new PersistenceStartupSignal();
        startup.Expect(typeof(StartupOrderingTests));
        startup.Fail(typeof(StartupOrderingTests), new InvalidProgramException("migration failed"));

        var service = new AuditLedgerSelfCheckHostedService(
            new AuditLedgerSelfCheck(new TestConnectionFactory("Host=invalid.invalid")),
            Microsoft.Extensions.Options.Options.Create(new AuditLedgerOptions { SelfCheck = AuditSelfCheckMode.Fail }),
            new CollectingLogger<AuditLedgerSelfCheckHostedService>(),
            startup);

        await FluentActions.Awaiting(() => service.StartedAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidProgramException>("the host reports the migration failure, not a self-check against no schema");
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
                Messages.Add(formatter(state, exception));
        }
    }
}
