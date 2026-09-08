using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// WO-053/P-333 (C-133): <see cref="EncryptionRotationService{TContext}"/>'s
/// <c>EncryptionRotationBatchProcessed</c>/<c>EncryptionRotationCompleted</c> Information logs
/// (EventIds <c>6009</c>-<c>6010</c>).
/// </summary>
public sealed class EncryptionRotationLoggingTests
{
    private static string MakeKey(byte fill)
    {
        var bytes = new byte[32];
        Array.Fill(bytes, fill);
        return Convert.ToBase64String(bytes);
    }

    private sealed class RotationLoggingTestHost : IDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required SqliteConnection Connection { get; init; }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }

    private static RotationLoggingTestHost BuildProvider(
        Action<EncryptionOptions> configure,
        InMemoryLogger<EncryptionRotationService<RotationTestDbContext>> logger)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();

        services
            .AddSharedKernelEfCore<RotationTestDbContext>(opts =>
                opts.UseSqlite(connection)
                    .ConfigureWarnings(w =>
                        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithEncryption(configure)
            .WithDbContextFactory()
            .Build();

        services.AddSingleton<ILogger<EncryptionRotationService<RotationTestDbContext>>>(logger);

        // D-131/P-498/WO-081: .WithEncryption() now builds its own persistence-scoped
        // ISymmetricEncryptionService internally, keyed-DI isolated — no consumer-side unkeyed
        // registration needed or wanted anymore.
        var provider = services.BuildServiceProvider();
        return new RotationLoggingTestHost { Provider = provider, Connection = connection };
    }

    [Fact]
    public async Task RotateAsync_LogsBatchProcessed_PerBatch_AndCompleted_Once()
    {
        // Arrange — BatchSize == 3 (LoggingTestEncryptionRotationService), 7 rows → 3 batches (3,3,1).
        var v1 = MakeKey(0x11);
        var v2 = MakeKey(0x22);

        var inMemoryLogger = new InMemoryLogger<EncryptionRotationService<RotationTestDbContext>>();
        using var host = BuildProvider(
            enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "1";
                enc.Keys["1"] = v1;
                enc.Keys["2"] = v2;
            },
            inMemoryLogger);

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        var clock = new SystemClock();

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            await ctx.Database.EnsureCreatedAsync();
            for (var i = 0; i < 7; i++)
                ctx.Customers.Add(new RotationCustomer(RotationCustomerId.New(), $"user{i}@example.com", clock));
            await ctx.SaveChangesAsync();
        }

        var rotationService = ActivatorUtilities.CreateInstance<LoggingTestEncryptionRotationService>(host.Provider);

        // Act
        var result = await rotationService.RotateAsync("1", "2");

        // Assert
        result.RowsProcessed.Should().Be(7);

        var batchRecords = inMemoryLogger.Records.Where(r => r.EventId.Id == 6009).ToList();
        batchRecords.Should().HaveCount(3);
        batchRecords.Should().OnlyContain(r => r.LogLevel == LogLevel.Information);

        // Batch boundaries are 3, 3, 1 rows (BatchSize == 3, 7 rows total) — BatchNumber (zero-based,
        // per EncryptionRotationService.RotateAsync's own counter) and RowsInBatch must reflect that
        // exact per-batch shape, in order.
        var expectedRowsPerBatch = new[] { 3, 3, 1 };
        for (var i = 0; i < batchRecords.Count; i++)
        {
            var record = batchRecords[i];
            record.TryGetProperty("BatchNumber", out var batchNumber).Should().BeTrue();
            batchNumber.Should().Be(i);
            record.TryGetProperty("RowsInBatch", out var rowsInBatch).Should().BeTrue();
            rowsInBatch.Should().Be(expectedRowsPerBatch[i]);
            record.TryGetProperty("FromVersion", out var fromVersion).Should().BeTrue();
            fromVersion.Should().Be("1");
            record.TryGetProperty("ToVersion", out var toVersion).Should().BeTrue();
            toVersion.Should().Be("2");
        }

        var completedRecord = inMemoryLogger.Records.ShouldHaveLogged(new EventId(6010), LogLevel.Information);
        completedRecord.TryGetProperty("RowsProcessed", out var rowsProcessed).Should().BeTrue();
        rowsProcessed.Should().Be(7);
        completedRecord.TryGetProperty("RowsRotated", out var rowsRotated).Should().BeTrue();
        rowsRotated.Should().Be(7);
        completedRecord.TryGetProperty("RowsFailed", out var rowsFailed).Should().BeTrue();
        rowsFailed.Should().Be(0);

        inMemoryLogger.Records.Count(r => r.EventId.Id == 6010).Should().Be(1);

        // Negative assertion (C-133): scan EVERY captured record's rendered message AND every
        // structured property value — none may ever equal a key byte, a Base64-encoded key
        // string, or (by construction of this fixture, whose only "sensitive" values are the two
        // keys) a plaintext/ciphertext value.
        foreach (var record in inMemoryLogger.Records)
        {
            record.Message.Should().NotContain(v1);
            record.Message.Should().NotContain(v2);

            if (record.State is null)
                continue;

            foreach (var kv in record.State)
            {
                kv.Value.Should().NotBe(v1);
                kv.Value.Should().NotBe(v2);
            }
        }
    }

    [Fact]
    public async Task RotateAsync_NoRows_LogsCompleted_WithZeroCounts_NoBatchLogs()
    {
        // Arrange
        var v1 = MakeKey(0x33);
        var v2 = MakeKey(0x44);

        var inMemoryLogger = new InMemoryLogger<EncryptionRotationService<RotationTestDbContext>>();
        using var host = BuildProvider(
            enc =>
            {
                enc.Enabled = true;
                enc.CurrentVersion = "1";
                enc.Keys["1"] = v1;
                enc.Keys["2"] = v2;
            },
            inMemoryLogger);

        var factory = host.Provider.GetRequiredService<IDbContextFactory<RotationTestDbContext>>();
        await using (var ctx = await factory.CreateDbContextAsync())
            await ctx.Database.EnsureCreatedAsync();

        var rotationService = ActivatorUtilities.CreateInstance<LoggingTestEncryptionRotationService>(host.Provider);

        // Act
        var result = await rotationService.RotateAsync("1", "2");

        // Assert
        result.RowsProcessed.Should().Be(0);
        inMemoryLogger.Records.ShouldNotHaveLogged(new EventId(6009));
        inMemoryLogger.Records.ShouldHaveLoggedWithProperty(new EventId(6010), "RowsProcessed", 0);
    }
}

/// <summary>
/// Test-local subclass forwarding <see cref="ILogger{TCategoryName}"/> so
/// <see cref="EncryptionRotationLoggingTests"/> can assert on the base class's logging —
/// <see cref="TestEncryptionRotationService"/> (the sibling fixture used by
/// <c>EncryptionRotationServiceTests</c>) deliberately omits the logger parameter.
/// </summary>
internal sealed class LoggingTestEncryptionRotationService : EncryptionRotationService<RotationTestDbContext>
{
    public LoggingTestEncryptionRotationService(
        IDbContextFactory<RotationTestDbContext> contextFactory,
        Microsoft.Extensions.Options.IOptionsMonitor<EncryptionOptions> optionsMonitor,
        EncryptedEntityBatchProcessorRegistry<RotationTestDbContext> registry,
        ILogger<EncryptionRotationService<RotationTestDbContext>> logger)
        : base(contextFactory, optionsMonitor, registry, logger)
    {
    }

    protected override int BatchSize => 3;
}
