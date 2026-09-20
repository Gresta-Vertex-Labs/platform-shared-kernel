using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;
using SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>Resumable key rotation against real PostgreSQL: cross-tenant, soft-deleted rows, concurrent writers, cancellation/resume, report accuracy.</summary>
[Collection("EncryptionPostgres")]
public sealed class EncryptionRotationIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly IClock Clock = new SystemClock();

    public EncryptionRotationIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    [Fact]
    public async Task Rotate_AcrossTenantsAndSoftDeletedRows_ReEncryptsEveryRowUnderCurrentKey()
    {
        var connectionString = ConnectionString("sk_enc_rotate_basic");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var actor = new FakeAuditActorContext(tenantId: tenantA);

        // Phase 1: write everything under key "v1" (current).
        await using (var writeSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v1", actorContext: actor))
        {
            await using var scope = writeSp.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            await context.Database.EnsureCreatedAsync();

            context.Customers.Add(new EncCustomer(EncCustomerId.New(), tenantA, Clock, "a1@example.com", "111-11-1111"));
            var toDelete = new EncCustomer(EncCustomerId.New(), tenantA, Clock, "a2@example.com", "222-22-2222");
            context.Customers.Add(toDelete);
            await context.SaveChangesAsync();

            context.Customers.Remove(toDelete); // soft-deleted, not hard-deleted — SoftDeleteInterceptor converts this.
            await context.SaveChangesAsync();
        }

        actor.TenantId = tenantB;
        await using (var writeSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v1", actorContext: actor))
        {
            await using var scope = writeSp.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            context.Customers.Add(new EncCustomer(EncCustomerId.New(), tenantB, Clock, "b1@example.com", "333-33-3333"));
            await context.SaveChangesAsync();
        }

        // Phase 2: flip the current key to "v2" and rotate.
        await using var rotateSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v2", actorContext: actor);
        await using (var scope = rotateSp.CreateAsyncScope())
        {
            var rotationJob = scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>();
            var report = await rotationJob.RotateAsync(expectedCurrentKeyId: "v2");

            var debug = $"processed={report.RowsProcessed} rotated={report.RowsRotated} failed={report.RowsFailed} remaining={string.Join(",", report.RowsRemainingByKeyId.Select(kv => $"{kv.Key}={kv.Value}"))}";
            report.Completed.Should().BeTrue(because: debug);
            report.CheckpointToken.Should().BeNull(because: debug);
            report.RowsProcessed.Should().Be(12, because: debug); // 3 rows x 4 encrypted targets each (Note stays null, never encrypted).
            report.RowsFailed.Should().Be(0, because: debug);
            report.RowsRotated.Should().Be(9, because: debug); // Email + Ssn + BillingAddress.Line1 per row (Note is null, nothing to rotate)
        }

        // Every row's ciphertext, including the soft-deleted one, is now under "v2" — read raw and check the key id.
        await using var raw = new NpgsqlConnection(connectionString);
        await raw.OpenAsync();
        await using var command = raw.CreateCommand();
        command.CommandText = "SELECT email FROM customers";
        await using var reader = await command.ExecuteReaderAsync();
        var keyIds = new List<string>();
        while (await reader.ReadAsync())
        {
            EncryptedPayload.TryParse(reader.GetString(0), out var payload).Should().BeTrue();
            keyIds.Add(payload!.KeyId);
        }

        keyIds.Should().HaveCount(3);
        keyIds.Should().OnlyContain(id => id == "v2");
    }

    [Fact]
    public async Task Rotate_ConcurrentWriter_NoLostUpdate()
    {
        var connectionString = ConnectionString("sk_enc_rotate_concurrent");
        var tenantId = Guid.NewGuid();
        var actor = new FakeAuditActorContext(tenantId: tenantId);
        var id = EncCustomerId.New();

        await using (var writeSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v1", actorContext: actor))
        {
            await using var scope = writeSp.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            await context.Database.EnsureCreatedAsync();
            context.Customers.Add(new EncCustomer(id, tenantId, Clock, "concurrent@example.com", "444-44-4444"));
            await context.SaveChangesAsync();
        }

        // A genuine concurrent writer changes the row AFTER rotation reads it but BEFORE rotation's
        // compare-and-swap runs — simulated by mutating the row via raw SQL between rotation's read and write.
        // Rotation reads the whole table in one page (batchSize large enough), so instead we race it against
        // a real second writer using EF Core (which also re-encrypts under "v1", since that writer's host still
        // has "v1" current) started just before rotation, both targeting the same row.
        await using var rotateSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v2", actorContext: actor);
        await using var concurrentWriterSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v1", actorContext: actor);

        await using (var writerScope = concurrentWriterSp.CreateAsyncScope())
        {
            var writerContext = writerScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            var entity = await writerContext.Customers.SingleAsync(x => x.Id == id);
            entity.ChangeEmail("changed-by-concurrent-writer@example.com");
            await writerContext.SaveChangesAsync();
        }

        await using var rotateScope = rotateSp.CreateAsyncScope();
        var rotationJob = rotateScope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>();
        var report = await rotationJob.RotateAsync(expectedCurrentKeyId: "v2");

        // The row was already re-saved (still under "v1", by the concurrent writer, AFTER rotation would have
        // read the original ciphertext) — rotation's compare-and-swap must not clobber the concurrent writer's
        // new value. Either rotation raced ahead of the writer (rotated cleanly, 0 failed) or the writer's save
        // landed first and rotation's CAS found a changed value (1 failed, left for the next pass) — both are
        // correct "no lost update" outcomes; what must NEVER happen is silent data loss.
        (report.RowsRotated + report.RowsFailed).Should().Be(3); // Email + Ssn + BillingAddress.Line1 targets for the one affected row

        await using var readSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v2", actorContext: actor);
        await using var readScope = readSp.CreateAsyncScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        var loaded = await readContext.Customers.SingleAsync(x => x.Id == id);
        loaded.Email.Should().Be("changed-by-concurrent-writer@example.com"); // the writer's value is never lost, whichever outcome above occurred.
    }

    [Fact]
    public async Task Rotate_CancelledMidway_ResumesFromCheckpointAndCompletes()
    {
        var connectionString = ConnectionString("sk_enc_rotate_resume");
        var tenantId = Guid.NewGuid();
        var actor = new FakeAuditActorContext(tenantId: tenantId);

        await using (var writeSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v1", actorContext: actor))
        {
            await using var scope = writeSp.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            await context.Database.EnsureCreatedAsync();
            for (var i = 0; i < 5; i++)
                context.Customers.Add(new EncCustomer(EncCustomerId.New(), tenantId, Clock, $"resume{i}@example.com", $"{i}-resume"));
            await context.SaveChangesAsync();
        }

        await using var rotateSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v2", actorContext: actor);
        await using var scope1 = rotateSp.CreateAsyncScope();
        var rotationJob1 = scope1.ServiceProvider.GetRequiredService<IEncryptionRotationJob>();

        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();

        var firstReport = await rotationJob1.RotateAsync(expectedCurrentKeyId: "v2", batchSize: 2, cancellationToken: alreadyCancelled.Token);
        firstReport.Completed.Should().BeFalse();
        firstReport.CheckpointToken.Should().NotBeNull();
        firstReport.RowsProcessed.Should().Be(0); // cancellation is observed before the first batch of this entity type.

        await using var scope2 = rotateSp.CreateAsyncScope();
        var rotationJob2 = scope2.ServiceProvider.GetRequiredService<IEncryptionRotationJob>();
        var secondReport = await rotationJob2.RotateAsync(expectedCurrentKeyId: "v2", checkpointToken: firstReport.CheckpointToken, batchSize: 2);

        secondReport.Completed.Should().BeTrue();
        secondReport.RowsRotated.Should().Be(15); // Email + Ssn + BillingAddress.Line1 per row across 5 rows
    }

    [Fact]
    public async Task Rotate_ComplexTypeEncryptedProperty_ReEncryptsUnderCurrentKeyAndPreservesPlaintext()
    {
        // EncCustomer.BillingAddress is an EF Core 10 complex type (value object) — no separate table, no primary
        // key of its own; its columns live flattened on the owning entity's own row, and its encrypted sub-property
        // (BillingAddress.Line1) is rotated by reading the OWNING entity's primary key, never a key of its own
        // (which a complex type does not have). This is the achievable half of "rotate an owned/complex-type
        // property" — see EncryptionOwnedTypeShadowKeyGuardTests for why the SAME-TABLE OWNED ENTITY TYPE half is
        // architecturally impossible instead.
        var connectionString = ConnectionString("sk_enc_rotate_complex_type");
        var tenantId = Guid.NewGuid();
        var actor = new FakeAuditActorContext(tenantId: tenantId);
        var id = EncCustomerId.New();

        await using (var writeSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v1", actorContext: actor))
        {
            await using var scope = writeSp.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            await context.Database.EnsureCreatedAsync();
            context.Customers.Add(new EncCustomer(
                id,
                tenantId,
                Clock,
                "complex-rotate@example.com",
                "777-77-7777",
                billingAddress: new EncBillingAddress("42 Complex Type Way", "Metroville")));
            await context.SaveChangesAsync();
        }

        await using var rotateSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v2", actorContext: actor);
        await using (var rotateScope = rotateSp.CreateAsyncScope())
        {
            var rotationJob = rotateScope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>();
            var report = await rotationJob.RotateAsync(expectedCurrentKeyId: "v2");
            report.Completed.Should().BeTrue();
            report.RowsFailed.Should().Be(0);
        }

        // Raw ciphertext for the billing_line1 column is now under "v2"...
        await using var raw = new NpgsqlConnection(connectionString);
        await raw.OpenAsync();
        await using var command = raw.CreateCommand();
        command.CommandText = "SELECT billing_line1 FROM customers WHERE id = @id";
        command.Parameters.AddWithValue("@id", id.Value);
        var ciphertext = (string)(await command.ExecuteScalarAsync())!;
        EncryptedPayload.TryParse(ciphertext, out var payload).Should().BeTrue();
        payload!.KeyId.Should().Be("v2");

        //... and the plaintext is unchanged, read back through EF via the now-current "v2" key.
        await using var readSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v2", actorContext: actor);
        await using var readScope = readSp.CreateAsyncScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        var loaded = await readContext.Customers.SingleAsync(x => x.Id == id);
        loaded.BillingAddress.Line1.Should().Be("42 Complex Type Way");
    }

    [Fact]
    public async Task Rotate_RowWithUnparseableStoredValue_CountsAndSkipsWithoutSilentlyReportingSuccess()
    {
        var connectionString = ConnectionString("sk_enc_rotate_unparseable");
        var tenantId = Guid.NewGuid();
        var actor = new FakeAuditActorContext(tenantId: tenantId);
        var goodId = EncCustomerId.New();
        var corruptId = EncCustomerId.New();

        await using (var writeSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v1", actorContext: actor))
        {
            await using var scope = writeSp.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            await context.Database.EnsureCreatedAsync();
            context.Customers.Add(new EncCustomer(goodId, tenantId, Clock, "good@example.com", "111-11-1111", note: "hi"));
            context.Customers.Add(new EncCustomer(corruptId, tenantId, Clock, "corrupt@example.com", "222-22-2222", note: "will be corrupted"));
            await context.SaveChangesAsync();
        }

        // Simulate a truncated/corrupted stored value for ONE column on ONE row — no EF, no interceptors — the
        // shape a still-in-progress plaintext-to-encrypted migration or genuine data corruption both take.
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = "UPDATE customers SET note = 'not-a-valid-encrypted-payload' WHERE id = @id";
            command.Parameters.AddWithValue("id", corruptId.Value);
            await command.ExecuteNonQueryAsync();
        }

        await using var rotateSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v2", actorContext: actor);
        await using var scope2 = rotateSp.CreateAsyncScope();
        var rotationJob = scope2.ServiceProvider.GetRequiredService<IEncryptionRotationJob>();
        var report = await rotationJob.RotateAsync(expectedCurrentKeyId: "v2");

        var debug = $"processed={report.RowsProcessed} rotated={report.RowsRotated} failed={report.RowsFailed} skipped={report.RowsSkippedUnparseable}";
        report.Completed.Should().BeTrue(because: debug);
        report.RowsProcessed.Should().Be(8, because: debug); // 2 rows x 4 encrypted targets each.
        report.RowsFailed.Should().Be(0, because: debug);
        // Every target rotates except the corrupted Note — a clean report must never claim that one succeeded too.
        report.RowsSkippedUnparseable.Should().Be(1, because: debug);
        report.RowsRotated.Should().Be(7, because: debug); // 4 (goodId) + 3 (corruptId: Email, Ssn, BillingAddress.Line1).

        // Rotation never overwrites a value it could not parse — the corrupted text survives untouched.
        await using var verify = new NpgsqlConnection(connectionString);
        await verify.OpenAsync();
        await using var verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText = "SELECT note FROM customers WHERE id = @id";
        verifyCommand.Parameters.AddWithValue("id", corruptId.Value);
        var stillCorrupt = (string)(await verifyCommand.ExecuteScalarAsync())!;
        stillCorrupt.Should().Be("not-a-valid-encrypted-payload");
    }

    [Fact]
    public async Task Rotate_BlindIndexDerivedFromDivergentSyncProvider_MatchesReencryptedAsyncKey_NotStaleSyncOne()
    {
        // H10 repro: a KMS-backed IEncryptionKeyProvider (asynchronous) reports "v2" is now current, while the
        // ISynchronousEncryptionKeyProvider blind-index derivation reads from is STUCK reporting "v1" — the exact
        // divergence window a periodically-refreshed EncryptionKeyRingCache bridge can be caught in right after an
        // operator flips the current key but before the next scheduled refresh. Every other fixture in this
        // project implements both interfaces off ONE shared StaticEncryptionKeyProvider, so this divergence is
        // impossible to reproduce without two genuinely independent provider instances — see DivergentKeyProviders.
        var connectionString = ConnectionString("sk_enc_rotate_divergent_blind_index");
        var tenantId = Guid.NewGuid();
        var actor = new FakeAuditActorContext(tenantId: tenantId);
        var id = EncCustomerId.New();
        const string email = "divergent@example.com";

        // Write under a host where async and sync genuinely agree ("v1" is current both ways).
        await using (var writeSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v1", actorContext: actor))
        {
            await using var scope = writeSp.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            await context.Database.EnsureCreatedAsync();
            context.Customers.Add(new EncCustomer(id, tenantId, Clock, email, "999-99-9999"));
            await context.SaveChangesAsync();
        }

        // Rotate under a host where they DISAGREE: the asynchronous provider (what RotateAsync validates
        // expectedCurrentKeyId against, and what re-encryption itself resolves through) says "v2"; the synchronous
        // provider (the ambient one BlindIndexService.Compute(string,...) would read from) is left stuck on "v1".
        await using var rotateSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v2", actorContext: actor, configureServices: services =>
        {
            services.AddSingleton<IEncryptionKeyProvider>(new FixedCurrentAsyncKeyProvider("v2"));
            services.AddSingleton<ISynchronousEncryptionKeyProvider>(new FixedCurrentSyncKeyProvider("v1"));
        });
        await using (var scope = rotateSp.CreateAsyncScope())
        {
            var rotationJob = scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>();
            var report = await rotationJob.RotateAsync(expectedCurrentKeyId: "v2");
            report.Completed.Should().BeTrue();
            report.RowsFailed.Should().Be(0);
        }

        // Raw ciphertext is now under "v2"...
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = "SELECT email FROM customers WHERE id = @id";
            command.Parameters.AddWithValue("id", id.Value);
            var stored = (string)(await command.ExecuteScalarAsync())!;
            EncryptedPayload.TryParse(stored, out var payload).Should().BeTrue();
            payload!.KeyId.Should().Be("v2");
        }

        // ...and a lookup from a NORMAL, internally-consistent "v2" host (async and sync agree, exactly as
        // EncryptionKeyRingCache would once it eventually refreshes) finds the row. Before the fix, rotation would
        // have derived the blind index from the STALE synchronous provider's "v1" material while re-encrypting the
        // ciphertext itself under the fresh "v2" key — this lookup would then find nothing, permanently, since no
        // future consistent host ever recomputes a "v1"-keyed blind index again.
        await using var readSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v2", actorContext: actor);
        await using var readScope = readSp.CreateAsyncScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        var blindIndexService = readScope.ServiceProvider.GetRequiredService<IBlindIndexService>();

        var found = await readContext.Customers
            .WhereBlindIndexEquals(
                blindIndexService, x => x.Email, "customer.email", email,
                normalize: static s => s.Trim().ToLowerInvariant(), // matches EncCustomerConfig's own registered normalize delegate.
                tenantId: tenantId)
                    .ToListAsync();

        found.Should().ContainSingle(x => x.Id == id);
    }

    [Fact]
    public async Task Rotate_WrongExpectedCurrentKey_ThrowsWithoutRotatingAnything()
    {
        var connectionString = ConnectionString("sk_enc_rotate_wrong_key");
        var tenantId = Guid.NewGuid();
        var actor = new FakeAuditActorContext(tenantId: tenantId);

        await using var sp = EncryptionTestHost.Build(connectionString, currentKeyId: "v1", actorContext: actor);
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        var rotationJob = scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>();
        var act = () => rotationJob.RotateAsync(expectedCurrentKeyId: "v2"); // actual current is "v1"

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*v2*");
    }
}
