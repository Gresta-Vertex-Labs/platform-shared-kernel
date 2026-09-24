using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace BillingApi.Tests;

/// <summary>
/// The persistence packages through the HTTP API of a real service. Every test uses its own tenants, so the tests share
/// one running service and database without seeing each other's data.
/// </summary>
[Collection(BillingApiCollection.Name)]
public sealed class BillingApiEndToEndTests(BillingApiFixture fixture)
{
    private HttpClient Tenant(Guid tenant, params string[] permissions) =>
        fixture.Client("user-" + tenant.ToString("N")[..6], tenant, permissions.Length == 0 ? [Http.Read, Http.Write] : permissions);

    // ---- Startup ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EveryTenantTable_IsProtectedByForcedRowLevelSecurity_FromTheMigration()
    {
        foreach (var table in new[] { "customers", "invoices", "invoice_lines", "payments" })
        {
            (await fixture.ScalarAsAdminAsync<bool>("SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = @t", ("t", table)))
                .Should().BeTrue($"'{table}' holds tenant data");
            (await fixture.ScalarAsAdminAsync<long>("SELECT count(*) FROM pg_policies WHERE tablename = @t", ("t", table)))
                .Should().Be(1, $"'{table}' has its tenant policy");
        }

        (await fixture.ScalarAsAdminAsync<bool>("SELECT relrowsecurity FROM pg_class WHERE relname = 'tax_rates'"))
            .Should().BeFalse("tax rates are tenant-shared reference data");
        (await fixture.ScalarAsAdminAsync<long>("SELECT count(*) FROM tax_rates")).Should().Be(3, "the seeder ran after the migration");
    }

    // ---- Encryption ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task PersonalData_IsEncryptedAtRest_AndFoundByValue_IgnoringCaseAndSpaces()
    {
        var client = Tenant(Guid.NewGuid());
        var id = await client.RegisterCustomerAsync("Ada Lovelace", "Ada.Lovelace@Example.com", "TR-1234567890");

        var stored = await fixture.ScalarAsAdminAsync<string>("SELECT email || '|' || tax_number FROM customers WHERE id = @id", ("id", id));
        stored.Should().NotContainEquivalentOf("lovelace").And.NotContain("1234567890");

        var found = await client.GetAsync("/customers?email=" + Uri.EscapeDataString("  ada.lovelace@EXAMPLE.com "));
        await found.EnsureAsync(HttpStatusCode.OK);
        var customer = await found.JsonAsync();
        customer.GetProperty("id").GetGuid().Should().Be(id);
        customer.GetProperty("email").GetString().Should().Be("Ada.Lovelace@Example.com");
        customer.GetProperty("taxNumber").GetString().Should().Be("TR-1234567890");

        var duplicate = await client.PostAsJsonAsync("/customers", new { name = "Again", email = "ADA.LOVELACE@example.com" });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- Multi-tenancy ------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnotherTenant_SeesNothing_ThroughEfCore_TheBlindIndex_OrHandWrittenSql()
    {
        var a = Tenant(Guid.NewGuid());
        var b = Tenant(Guid.NewGuid());
        var customer = await a.RegisterCustomerAsync("Tenant A customer", "shared@example.com");
        var invoice = await a.DraftInvoiceAsync(customer);
        await (await a.PostAsync($"/invoices/{invoice}/issue", null)).EnsureAsync(HttpStatusCode.NoContent);

        (await b.GetAsync($"/customers/{customer}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.GetAsync($"/invoices/{invoice}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.GetAsync("/customers?email=shared@example.com")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await b.GetAsync("/invoices?page=1")).JsonAsync()).GetProperty("totalCount").GetInt64().Should().Be(0);

        // Dapper, no tenant predicate in the SQL: only row-level security keeps tenant A's invoice out.
        var revenue = await (await b.GetAsync("/reports/revenue")).JsonAsync();
        revenue.GetArrayLength().Should().Be(0);

        // Writing against another tenant's aggregate is "not found", never a leak of its existence.
        (await b.PostAsync($"/invoices/{invoice}/issue", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The same email is free in another tenant: blind indexes are tenant-bound.
        await b.RegisterCustomerAsync("Tenant B customer", "shared@example.com");
    }

    [Fact]
    public async Task BackOffice_ReportsAcrossTenants_OnlyWithTheAdminPermission()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        foreach (var tenant in new[] { a, b })
        {
            var client = Tenant(tenant);
            var invoice = await client.DraftInvoiceAsync(await client.RegisterCustomerAsync("C", $"c-{tenant:N}@example.com"));
            await (await client.PostAsync($"/invoices/{invoice}/issue", null)).EnsureAsync(HttpStatusCode.NoContent);
        }

        (await Tenant(a).GetAsync("/admin/reports/revenue-by-tenant")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var report = await (await fixture.Client("back-office", tenant: null, Http.Admin).GetAsync("/admin/reports/revenue-by-tenant")).JsonAsync();
        var tenants = report.EnumerateArray().Select(r => r.GetProperty("tenantId").GetGuid()).ToList();
        tenants.Should().Contain([a, b]);
    }

    // ---- Authorization ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Anonymous_Is401_AndAMissingPermission_Is403()
    {
        // Permissions declared by the command or query (IAuthorizeRequest), checked by the pipeline.
        (await fixture.Anonymous().GetAsync("/invoices?page=1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var readOnly = Tenant(Guid.NewGuid(), Http.Read);
        (await readOnly.PostAsJsonAsync("/customers", new { name = "x", email = "x@example.com" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await readOnly.GetAsync("/invoices?page=1")).StatusCode.Should().Be(HttpStatusCode.OK);

        // A permission declared on the route (RequirePermission), checked before any handler runs — same answers, as problems.
        var anonymous = await fixture.Anonymous().GetAsync("/admin/reports/revenue-by-tenant");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.ErrorCodeAsync()).Should().Be(ErrorCodes.Unauthorized.Default);

        var forbidden = await readOnly.GetAsync("/admin/reports/revenue-by-tenant");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        forbidden.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await forbidden.ErrorCodeAsync()).Should().Be(ErrorCodes.Forbidden.InsufficientPermission);
    }

    // ---- Optimistic concurrency ---------------------------------------------------------------------------------

    [Fact]
    public async Task ETag_IfMatch_RoundTrip()
    {
        var client = Tenant(Guid.NewGuid());
        var id = await client.RegisterCustomerAsync("Grace", "grace@example.com");

        var read = await client.GetAsync($"/customers/{id}");
        var etag = read.Headers.ETag!;
        etag.Tag.Should().NotBe("\"0\"", "the version is the row's xmin, never a placeholder");

        // P-562 X4: the ETag is an opaque token — the xmin sealed under the service's key — so it does not reveal how many
        // transactions the shared database committed; and the raw xmin is not accepted as a version.
        var xmin = await fixture.ScalarAsAdminAsync<string>("SELECT xmin::text FROM customers WHERE id = @id", ("id", id));
        etag.Tag.Should().HaveLength(30).And.NotContain(xmin!);
        var raw = new HttpRequestMessage(HttpMethod.Put, $"/customers/{id}/name") { Content = JsonContent.Create(new { name = "Raw" }) };
        raw.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{xmin}\""));
        (await client.SendAsync(raw)).StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);

        // A client that already holds the current version is told so, without the body.
        var revalidate = new HttpRequestMessage(HttpMethod.Get, $"/customers/{id}");
        revalidate.Headers.IfNoneMatch.Add(etag);
        var notModified = await client.SendAsync(revalidate);
        notModified.StatusCode.Should().Be(HttpStatusCode.NotModified);
        notModified.Headers.ETag.Should().Be(etag);

        var unconditional = await client.PutAsJsonAsync($"/customers/{id}/name", new { name = "No precondition" });
        unconditional.StatusCode.Should().Be((HttpStatusCode)428);
        (await unconditional.ErrorCodeAsync()).Should().Be(PresentationErrorCodes.PreconditionRequired);

        var rename = new HttpRequestMessage(HttpMethod.Put, $"/customers/{id}/name") { Content = JsonContent.Create(new { name = "Grace Hopper" }) };
        rename.Headers.IfMatch.Add(etag);
        var renamed = await client.SendAsync(rename);
        await renamed.EnsureAsync(HttpStatusCode.OK);
        (await renamed.JsonAsync()).GetProperty("name").GetString().Should().Be("Grace Hopper");
        var newTag = renamed.Headers.ETag!;
        newTag.Should().NotBe(etag);

        // The save's ConflictException becomes 412 on an endpoint that requires If-Match, keeping its error code.
        var stale = new HttpRequestMessage(HttpMethod.Put, $"/customers/{id}/name") { Content = JsonContent.Create(new { name = "Lost update" }) };
        stale.Headers.IfMatch.Add(etag);
        var rejected = await client.SendAsync(stale);
        rejected.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        rejected.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await rejected.ErrorCodeAsync()).Should().Be(ConcurrencyVersion.ConflictErrorCode);

        // A tag this API never issued cannot name the current version either.
        var unknown = new HttpRequestMessage(HttpMethod.Put, $"/customers/{id}/name") { Content = JsonContent.Create(new { name = "Guess" }) };
        unknown.Headers.IfMatch.Add(new EntityTagHeaderValue("\"not-a-version\""));
        (await client.SendAsync(unknown)).StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);

        // The client re-reads for the current version and retries from there.
        var current = await client.GetAsync($"/customers/{id}");
        current.Headers.ETag.Should().Be(newTag);
        (await current.JsonAsync()).GetProperty("name").GetString().Should().Be("Grace Hopper");
    }

    // ---- Commands: transactions, domain events, Dapper + EF Core in one unit of work ----------------------------

    [Fact]
    public async Task InvoiceLifecycle_IssueRaisesADomainEvent_AndPaymentCommitsDapperAndEfTogether()
    {
        var client = Tenant(Guid.NewGuid());
        var customer = await client.RegisterCustomerAsync("Linus", "linus@example.com");
        var invoice = await client.DraftInvoiceAsync(customer, "STD", ("Consulting", 3, 100.50m), ("Travel", 1, 49.50m));

        var draft = await (await client.GetAsync($"/invoices/{invoice}")).JsonAsync();
        draft.GetProperty("number").GetString().Should().Be("INV-00001", "numbers are per tenant");
        draft.GetProperty("net").GetDecimal().Should().Be(351.00m);
        draft.GetProperty("lines").GetArrayLength().Should().Be(2, "the lines are loaded with the aggregate");

        await (await client.PostAsync($"/invoices/{invoice}/issue", null)).EnsureAsync(HttpStatusCode.NoContent);
        (await (await client.GetAsync($"/invoices/{invoice}")).JsonAsync()).GetProperty("gross").GetDecimal().Should().Be(421.20m);
        (await (await client.GetAsync($"/customers/{customer}")).JsonAsync()).GetProperty("invoiceCount").GetInt32()
            .Should().Be(1, "the InvoiceIssued handler changed the customer in the same save");

        await (await client.PostAsJsonAsync($"/invoices/{invoice}/payments", new { amount = 421.20m, reference = "SEPA-1" }))
            .EnsureAsync(HttpStatusCode.NoContent);

        (await fixture.ScalarAsAdminAsync<decimal>("SELECT sum(amount) FROM payments WHERE invoice_id = @id", ("id", invoice))).Should().Be(421.20m);
        (await (await client.GetAsync($"/invoices/{invoice}")).JsonAsync()).GetProperty("status").GetString().Should().Be("Paid");

        var revenue = (await (await client.GetAsync("/reports/revenue")).JsonAsync())[0];
        revenue.GetProperty("gross").GetDecimal().Should().Be(421.20m);
        revenue.GetProperty("received").GetDecimal().Should().Be(421.20m);
    }

    [Fact]
    public async Task AFailedCommand_RollsBackItsDapperWrite_AndIsAuditedAsFailed()
    {
        var client = Tenant(Guid.NewGuid());
        var invoice = await client.DraftInvoiceAsync(await client.RegisterCustomerAsync("Ken", "ken@example.com"));
        await (await client.PostAsync($"/invoices/{invoice}/issue", null)).EnsureAsync(HttpStatusCode.NoContent);

        // The handler inserts the payment row, then the aggregate refuses the amount: the failed Result rolls back both.
        var wrong = await client.PostAsJsonAsync($"/invoices/{invoice}/payments", new { amount = 1m, reference = "short" });
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrong.JsonAsync()).GetProperty("errorCode").GetString().Should().Be("invoice.payment.amount");

        (await fixture.ScalarAsAdminAsync<long>("SELECT count(*) FROM payments WHERE invoice_id = @id", ("id", invoice))).Should().Be(0);

        var history = await (await client.GetAsync($"/audit/Invoice/{invoice}")).JsonAsync();
        history.EnumerateArray().Select(r => (r.GetProperty("action").GetString(), r.GetProperty("outcome").GetString()))
            .Should().Equal(("invoice.drafted", "Succeeded"), ("invoice.issued", "Succeeded"), ("invoice.paid", "Failed"));
    }

    // ---- Reads: offset and keyset paging ------------------------------------------------------------------------

    [Fact]
    public async Task Paging_OffsetPages_AndCursorPages_WithoutOverlap()
    {
        var client = Tenant(Guid.NewGuid());
        var customer = await client.RegisterCustomerAsync("Pager", "pager@example.com");
        var created = new List<Guid>();
        for (var i = 0; i < 5; i++)
            created.Add(await client.DraftInvoiceAsync(customer));

        var page = await (await client.GetAsync("/invoices?page=2&pageSize=2")).JsonAsync();
        page.GetProperty("totalCount").GetInt64().Should().Be(5);
        page.GetProperty("items").GetArrayLength().Should().Be(2);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = "/invoices/browse?limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var slice = await (await client.GetAsync(url)).JsonAsync();
            seen.AddRange(slice.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
            cursor = slice.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        seen.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(created);

        (await client.GetAsync("/invoices/browse?cursor=not-a-cursor")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/invoices?pageSize=5000")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Bulk update and soft delete ----------------------------------------------------------------------------

    [Fact]
    public async Task BulkExpire_UpdatesOnlyThisTenantsDrafts_AndStampsTheActor()
    {
        var mine = Tenant(Guid.NewGuid());
        var other = Tenant(Guid.NewGuid());
        var customer = await mine.RegisterCustomerAsync("Bulk", "bulk@example.com");
        var draft = await mine.DraftInvoiceAsync(customer);
        var issued = await mine.DraftInvoiceAsync(customer);
        await (await mine.PostAsync($"/invoices/{issued}/issue", null)).EnsureAsync(HttpStatusCode.NoContent);
        var othersDraft = await other.DraftInvoiceAsync(await other.RegisterCustomerAsync("Other", "other@example.com"));

        var result = await (await mine.PostAsJsonAsync("/invoices/expire-drafts", new { draftedBefore = DateTimeOffset.UtcNow.AddMinutes(1) })).JsonAsync();
        result.GetProperty("expired").GetInt32().Should().Be(1);

        (await fixture.ScalarAsAdminAsync<string>("SELECT status || '|' || modified_by FROM invoices WHERE id = @id", ("id", draft)))
            .Should().StartWith("Expired|user-");
        (await fixture.ScalarAsAdminAsync<string>("SELECT status FROM invoices WHERE id = @id", ("id", issued))).Should().Be("Issued");
        (await fixture.ScalarAsAdminAsync<string>("SELECT status FROM invoices WHERE id = @id", ("id", othersDraft))).Should().Be("Draft");
    }

    [Fact]
    public async Task Delete_IsSoft_TheRowStays_AndIsHiddenFromEveryRead()
    {
        var client = Tenant(Guid.NewGuid());
        var id = await client.RegisterCustomerAsync("Temp", "temp@example.com");
        var etag = (await client.GetAsync($"/customers/{id}")).Headers.ETag!;

        // Like a rename, a delete names the version it removes.
        (await client.DeleteAsync($"/customers/{id}")).StatusCode.Should().Be((HttpStatusCode)428);

        var delete = new HttpRequestMessage(HttpMethod.Delete, $"/customers/{id}");
        delete.Headers.IfMatch.Add(etag);
        await (await client.SendAsync(delete)).EnsureAsync(HttpStatusCode.NoContent);

        (await client.GetAsync($"/customers/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/customers?email=temp@example.com")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fixture.ScalarAsAdminAsync<string>("SELECT is_deleted || '|' || deleted_by FROM customers WHERE id = @id", ("id", id)))
            .Should().StartWith("true|user-");
    }

    // ---- Audit trail --------------------------------------------------------------------------------------------

    [Fact]
    public async Task AuditTrail_IsSealedByTheSealerRole_AndVerifiesIntact()
    {
        var client = Tenant(Guid.NewGuid());
        var id = await client.RegisterCustomerAsync("Audited", "audited@example.com");

        var history = await (await client.GetAsync($"/audit/Customer/{id}")).JsonAsync();
        var record = history.EnumerateArray().Single();
        record.GetProperty("action").GetString().Should().Be("customer.registered");
        record.GetProperty("actorKind").GetString().Should().Be("User");

        // The sealer links records in the background (every 2 s) as app_audit_sealer.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await fixture.ScalarAsAdminAsync<long>(
                   "SELECT count(*) FROM audit_records r WHERE r.resource_id = @id AND NOT EXISTS (SELECT 1 FROM audit_chain_links l WHERE l.record_id = r.id)",
                   ("id", id.ToString())) > 0)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the sealer must keep up");
            await Task.Delay(250);
        }

        var chain = await (await client.GetAsync("/audit/Customer")).JsonAsync();
        chain.GetProperty("status").GetString().Should().Be("Intact");
        chain.GetProperty("recordsChecked").GetInt64().Should().Be(1);

        // Append-only for the application role: triggers refuse any change, whoever asks.
        var tamper = async () => await fixture.ScalarAsAdminAsync<long>("UPDATE audit_records SET action = 'x' WHERE resource_id = @id RETURNING 1", ("id", id.ToString()));
        await tamper.Should().ThrowAsync<Npgsql.PostgresException>();
    }

    // ---- Encryption maintenance and crypto-shredding ------------------------------------------------------------

    [Fact]
    public async Task EncryptionMaintenance_VerifiesEveryValue()
    {
        var client = Tenant(Guid.NewGuid());
        await client.RegisterCustomerAsync("Maintained", "maintained@example.com", "TR-42");

        await using var scope = fixture.Services.CreateAsyncScope();
        using (scope.ServiceProvider.GetRequiredService<ICrossTenantScope>().Enter("encryption verification (test)"))
        {
            var report = await scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>()
                .RunAsync(new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.VerifyOnly });

            report.Completed.Should().BeTrue();
            report.UndecryptableValues.Should().Be(0);
            report.PlaintextValues.Should().Be(0);
            report.StaleBlindIndexes.Should().Be(0);
            report.ValuesByKeyId.Values.Sum().Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task ErasingATenant_MakesItsEncryptedDataUnreadable_AndLeavesOtherTenantsAlone()
    {
        var erased = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var erasedClient = Tenant(erased);
        var keptClient = Tenant(kept);
        var gone = await erasedClient.RegisterCustomerAsync("Forget me", "forget@example.com");
        var stays = await keptClient.RegisterCustomerAsync("Keep me", "keep@example.com");

        (await fixture.Client("dpo", tenant: null).PostAsync($"/admin/tenants/{erased}/erase", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var erasure = await fixture.Client("dpo", tenant: null, Http.Admin).PostAsync($"/admin/tenants/{erased}/erase", null);
        await erasure.EnsureAsync(HttpStatusCode.OK);
        (await erasure.JsonAsync()).GetProperty("isComplete").GetBoolean().Should().BeTrue();

        var read = await erasedClient.GetAsync($"/customers/{gone}");
        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await read.JsonAsync()).GetProperty("errorCode").GetString().Should().Be("Persistence.Encryption.TenantKeyShredded");
        (await erasedClient.PostAsJsonAsync("/customers", new { name = "Back", email = "back@example.com" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await keptClient.GetAsync($"/customers/{stays}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Readiness ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Readiness_ReportsTheDatabaseKeyRingAndSealer()
    {
        var ready = await fixture.Anonymous().GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
