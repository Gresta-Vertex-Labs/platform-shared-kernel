using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;
using SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>
/// H11 coverage: a rotation checkpoint produced against one shape of the model must resume correctly against a
/// LATER, EXPANDED shape of the model — the exact situation a real deploy creates when it adds a new
/// <c>.Encrypt(...)</c> entity type between a cancelled rotation call and the one that resumes it.
/// </summary>
/// <remarks>
/// The checkpoint here is built directly (via <c>EncryptionRotationCheckpoint</c>'s internal constructor, reachable
/// through this test project's <c>InternalsVisibleTo</c>) rather than by racing a live cancellation against
/// in-flight I/O. That race was tried first and is NOT reliable against this service's actual cancellation
/// granularity: <c>RotateAsync</c> polls <c>cancellationToken.IsCancellationRequested</c> only once per PAGE, and
/// every ADO.NET call within a page (the page read itself, plus one re-encrypt-and-compare-and-swap round trip PER
/// ROW needing rotation) shares that SAME token — Npgsql throws <c>OperationCanceledException</c> immediately from
/// whichever one of those calls happens to be starting or in flight when the token flips, rather than letting it
/// complete and only being observed at the next poll. A wall-clock delay cannot reliably land in the narrow
/// between-awaits window that would avoid this. The checkpoint built here is exactly the shape a genuine
/// cancellation between two targets would have produced (a name-keyed, fully "this target already scanned" record
/// for every target REAL, uncancelled rotation already completed) — it exercises the identical resume/decode path,
/// with certainty instead of a race.
/// </remarks>
[Collection("EncryptionPostgres")]
public sealed class EncryptionRotationCheckpointIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly IClock Clock = new SystemClock();

    public EncryptionRotationCheckpointIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    [Fact]
    public async Task Rotate_ResumedUnderExpandedModel_SkipsAlreadyCompletedTargetsByNameAndStillRotatesTheNewEntityType()
    {
        var connectionString = ConnectionString("sk_enc_rotate_model_change");
        var tenantId = Guid.NewGuid();
        var actor = new FakeAuditActorContext(tenantId: tenantId);

        // Provision the FULL (V2) schema upfront — both "customers" and "orders" — mirroring a real deploy where
        // migrations create a new feature's tables before any code path referencing them goes live. EnsureCreatedAsync
        // is a whole-database-level idempotency check, not per-table, so it MUST run against the widest model first;
        // running it again later from the narrower (V1) context is a safe no-op against an already-existing database.
        string customerEntityTypeName;
        await using (var schemaSp = EncryptionTestHost.Build<EncryptionTestDbContextV2>(connectionString, actorContext: actor))
        {
            await using var scope = schemaSp.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContextV2>();
            await context.Database.EnsureCreatedAsync();
            customerEntityTypeName = context.Model.FindEntityType(typeof(EncCustomer))!.Name;
        }

        const int customerCount = 5;
        const int orderCount = 5;

        // "Existing data, written by the OLD deploy" — EncCustomer only, key "v1".
        await using (var writeSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v1", actorContext: actor))
        {
            await using var scope = writeSp.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            for (var i = 0; i < customerCount; i++)
                context.Customers.Add(new EncCustomer(EncCustomerId.New(), tenantId, Clock, $"model-change-{i}@example.com", $"{i:000}-00-0000"));
            await context.SaveChangesAsync();
        }

        // "New feature shipped by the NEW deploy, with data of its own already written" — EncAaaOrder, also key "v1".
        await using (var writeSp = EncryptionTestHost.Build<EncryptionTestDbContextV2>(connectionString, currentKeyId: "v1", actorContext: actor))
        {
            await using var scope = writeSp.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContextV2>();
            for (var i = 0; i < orderCount; i++)
                context.Orders.Add(new EncAaaOrder(EncAaaOrderId.New(), tenantId, Clock, $"order note {i}"));
            await context.SaveChangesAsync();
        }

        // CALL #1: a REAL, uncancelled rotation under the OLD (V1) model — EncryptionTestDbContext's
        // IEncryptionRotationJob has never heard of EncAaaOrder at all; it structurally CANNOT touch it, matching
        // "the code running this rotation predates the Orders feature". Runs to completion, genuinely rotating
        // every EncCustomer target.
        await using (var rotateSp = EncryptionTestHost.Build(connectionString, currentKeyId: "v2", actorContext: actor))
        {
            await using var scope = rotateSp.CreateAsyncScope();
            var rotationJob = scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>();
            var firstReport = await rotationJob.RotateAsync(expectedCurrentKeyId: "v2");
            firstReport.Completed.Should().BeTrue();
            firstReport.RowsFailed.Should().Be(0);
        }

        // The checkpoint a cancellation landing exactly between EncCustomer (fully done) and the next target would
        // have produced: every EncCustomer target genuinely completed above — TRUTHFULLY, not merely claimed — is
        // named here, nothing in progress. Property paths mirror EncCustomer's four '.Encrypt(...)' targets
        // (Email, Ssn, Note, BillingAddress.Line1), the SAME set every other rotation test in this project asserts.
        var completedTargetKeys = new[] { "Email", "Ssn", "Note", "BillingAddress.Line1" }
            .Select(propertyPath => $"{customerEntityTypeName}::{propertyPath}")
                .ToArray();
        var checkpointToken = new EncryptionRotationCheckpoint(completedTargetKeys, InProgressTargetKey: null, LastPrimaryKeyText: "").Encode();

        // CALL #2: resume under the EXPANDED (V2) model. EncAaaOrder's target now sorts alphabetically BEFORE
        // every EncCustomer target in BuildTargets' freshly rebuilt, alphabetically-ordered list — a positional
        // checkpoint from call #1 would now point at the WRONG target (or skip past EncAaaOrder's target entirely,
        // or land at an EncCustomer target that had already finished). A name-keyed checkpoint is immune to this:
        // every EncCustomer target is found by name and skipped (it is genuinely done — the ASSERTION below on
        // RowsProcessed proves the skip actually happened, not just that the end state looks right), and
        // EncAaaOrder — absent from the checkpoint entirely, since it did not exist in call #1's model — is
        // neither "completed" nor "in progress", so it is scanned from the beginning like any other
        // never-before-seen target.
        await using var resumeSp = EncryptionTestHost.Build<EncryptionTestDbContextV2>(connectionString, currentKeyId: "v2", actorContext: actor);
        await using (var scope = resumeSp.CreateAsyncScope())
        {
            var rotationJob = scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>();
            var secondReport = await rotationJob.RotateAsync(expectedCurrentKeyId: "v2", checkpointToken: checkpointToken);

            var debug = $"processed={secondReport.RowsProcessed} rotated={secondReport.RowsRotated} failed={secondReport.RowsFailed}";
            secondReport.Completed.Should().BeTrue(because: debug);
            secondReport.RowsFailed.Should().Be(0, because: debug);
            // Every EncCustomer target was skipped by name (0 pages read for any of them) — only EncAaaOrder's
            // single target (one row per order) was actually scanned. If the checkpoint's completed-by-name skip
            // did not work, this would be customerCount x 4 higher.
            secondReport.RowsProcessed.Should().Be(orderCount, because: debug);
            secondReport.RowsRotated.Should().Be(orderCount, because: debug);
        }

        // Every EncCustomer row's Email/Ssn/BillingAddress.Line1 (Note stays null, never a target) is on "v2" —
        // proving CALL #1's real rotation actually happened (the premise the manually-built checkpoint depends on
        // being true).
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = "SELECT email, ssn, billing_line1 FROM customers";
            await using var reader = await command.ExecuteReaderAsync();
            var rowCount = 0;
            while (await reader.ReadAsync())
            {
                rowCount++;
                for (var column = 0; column < 3; column++)
                {
                    EncryptedPayload.TryParse(reader.GetString(column), out var payload).Should().BeTrue();
                    payload!.KeyId.Should().Be("v2", because: $"column {column} of row {rowCount} must be fully rotated");
                }
            }

            rowCount.Should().Be(customerCount);
        }

        // Every EncAaaOrder row's Note is ALSO on "v2" — the crux of H11: a target that did not exist when the
        // checkpoint was produced must still be reached and fully rotated on resume, never silently skipped while
        // the report still claims Completed=true.
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = "SELECT note FROM orders";
            await using var reader = await command.ExecuteReaderAsync();
            var rowCount = 0;
            while (await reader.ReadAsync())
            {
                rowCount++;
                EncryptedPayload.TryParse(reader.GetString(0), out var payload).Should().BeTrue();
                payload!.KeyId.Should().Be("v2", because: $"orders row {rowCount} (a target absent from call #1's model) must still be rotated");
            }

            rowCount.Should().Be(orderCount);
        }
    }
}
