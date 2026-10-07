using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Grpc.Core;
using SharedKernel.Primitives.Propagation;
using Shop.AppHost;
using Shop.Contracts.Inventory;
using Shop.E2E.Infrastructure;
using Xunit;

namespace Shop.E2E;

/// <summary>
/// Inventory: Dapper under row-level security, gRPC over mutual TLS with a certificate allow-list, the tenant carried by
/// a header only service callers may use, Redis locks against overselling across replicas, Redis hashes, and a job
/// scheduled on two replicas that runs once per occurrence.
/// </summary>
[Collection(ShopPlatformCollection.Name)]
public sealed class InventoryFlowTests(ShopPlatform platform)
{
    private const string Alice = ShopResources.Identity.ContosoMerchant;
    private const string Contoso = ShopResources.Identity.ContosoTenant;
    private const string Fabrikam = ShopResources.Identity.FabrikamTenant;

    [E2EFact]
    public async Task Merchant_SetsStock_AndReadsItBack()
    {
        using var alice = await MerchantAsync();
        string sku = Unique();

        (await alice.PutAsJsonAsync($"/stock/{sku}", new { OnHand = 12 }))
            .StatusCode.Should()
            .Be(HttpStatusCode.NoContent);
        var level = await alice.GetFromJsonAsync<Level>($"/stock/{sku}");

        level.Should().Be(new Level(sku, 12, 0, 12));
    }

    [E2EFact]
    public async Task Ordering_ReservesAndReleases_OverMutualTls_Idempotently()
    {
        using var alice = await MerchantAsync();
        string sku = Unique();
        await SetStockAsync(alice, sku, 10);
        var inventory = Ordering(ShopResources.Inventory);
        string orderId = Guid.NewGuid().ToString();

        var first = await inventory.ReserveStockAsync(Reserve(orderId, sku, 3), Tenant(Contoso));
        var again = await inventory.ReserveStockAsync(Reserve(orderId, sku, 3), Tenant(Contoso));

        again
            .ReservationId.Should()
            .Be(first.ReservationId, "a retried reservation returns the first one");
        (await alice.GetFromJsonAsync<Level>($"/stock/{sku}"))!.Reserved.Should().Be(3);

        var released = await inventory.ReleaseStockAsync(
            new ReleaseStockRequest { OrderId = orderId },
            Tenant(Contoso)
        );
        released.ReleasedLines.Should().Be(1);
        (await alice.GetFromJsonAsync<Level>($"/stock/{sku}"))!.Reserved.Should().Be(0);
    }

    [E2EFact]
    public async Task ConcurrentReservations_AcrossBothReplicas_NeverOversell()
    {
        using var alice = await MerchantAsync();
        string sku = Unique();
        await SetStockAsync(alice, sku, 5);
        var replicas = new[]
        {
            Ordering(ShopResources.Inventory),
            Ordering(ShopResources.Inventory2),
        };

        var attempts = Enumerable
            .Range(0, 20)
            .Select(async i =>
            {
                try
                {
                    await replicas[i % 2]
                        .ReserveStockAsync(
                            Reserve(Guid.NewGuid().ToString(), sku, 1),
                            Tenant(Contoso)
                        );
                    return StatusCode.OK;
                }
                catch (RpcException ex)
                {
                    return ex.StatusCode;
                }
            });
        var outcomes = await Task.WhenAll(attempts);

        outcomes
            .Count(code => code == StatusCode.OK)
            .Should()
            .Be(5, "exactly the five units on hand can be reserved");
        outcomes
            .Where(code => code != StatusCode.OK)
            .Should()
            .OnlyContain(
                code => code == StatusCode.FailedPrecondition || code == StatusCode.Aborted,
                "a refused reservation is out of stock (FailedPrecondition) or lost the lock race (Aborted)"
            );
        (await alice.GetFromJsonAsync<Level>($"/stock/{sku}"))!
            .Should()
            .Be(new Level(sku, 5, 5, 0));
    }

    [E2EFact]
    public async Task Reservation_IsTenantScoped_ByTheHeaderAServiceSends()
    {
        using var alice = await MerchantAsync();
        string sku = Unique();
        await SetStockAsync(alice, sku, 4);

        var asFabrikam = async () =>
            await Ordering(ShopResources.Inventory)
                .ReserveStockAsync(Reserve(Guid.NewGuid().ToString(), sku, 1), Tenant(Fabrikam));

        (await asFabrikam.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should()
            .Be(StatusCode.NotFound, "row-level security hides Contoso's SKU from Fabrikam");
    }

    [E2EFact]
    public async Task CallerWithoutACertificate_CannotReserve()
    {
        var anonymous = new InventoryService.InventoryServiceClient(
            platform.GrpcChannel(ShopResources.Inventory, client: null)
        );

        var act = async () =>
            await anonymous.ReserveStockAsync(
                Reserve(Guid.NewGuid().ToString(), Unique(), 1),
                Tenant(Contoso)
            );

        (await act.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should()
            .BeOneOf(StatusCode.Unauthenticated, StatusCode.PermissionDenied);
    }

    [E2EFact]
    public async Task CertificateFromTheShopCa_ButNotAllowListed_CannotReserve()
    {
        var rogue = new InventoryService.InventoryServiceClient(
            platform.GrpcChannel(ShopResources.Inventory, ShopResources.Clients.Rogue)
        );

        // The ServiceDefaults mTLS wiring runs the validator during the TLS handshake, so a rejected certificate is
        // usually refused there (the client sees Unavailable) before any request reaches the server.
        var act = async () =>
            await rogue.ReserveStockAsync(
                Reserve(Guid.NewGuid().ToString(), Unique(), 1),
                Tenant(Contoso)
            );

        (await act.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should()
            .BeOneOf(
                StatusCode.Unauthenticated,
                StatusCode.PermissionDenied,
                StatusCode.Unavailable
            );
    }

    [E2EFact]
    public async Task User_CannotChooseATenant_WithTheServiceHeader()
    {
        // Alice's token says Contoso. Sending Fabrikam's id in X-Tenant-Id changes nothing: only a certificate caller's
        // header is read, and the signed claim comes first anyway.
        using var alice = await MerchantAsync();
        string sku = Unique();
        await SetStockAsync(alice, sku, 7);

        using var spoofing = await MerchantAsync();
        spoofing.DefaultRequestHeaders.Add(WellKnownHeaders.TenantId, Fabrikam);

        (await spoofing.GetFromJsonAsync<Level>($"/stock/{sku}"))!.OnHand.Should().Be(7);
    }

    [E2EFact]
    public async Task ReconciliationJob_ScheduledOnBothReplicas_RunsOncePerOccurrence()
    {
        using var alice = await MerchantAsync();
        await Task.Delay(TimeSpan.FromSeconds(9));

        var runs = await alice.GetFromJsonAsync<List<JobRun>>("/ops/job-runs");

        runs.Should().HaveCountGreaterThanOrEqualTo(3, "the job fires every two seconds");
        runs!
            .GroupBy(run => run.FireTime)
            .Should()
            .OnlyContain(
                group => group.Count() == 1,
                "the scheduler's lease lets exactly one replica run each occurrence"
            );
    }

    private async Task<HttpClient> MerchantAsync() =>
        await platform.ClientAsync(ShopResources.Inventory, Alice, endpoint: "http");

    private InventoryService.InventoryServiceClient Ordering(string replica) =>
        new(platform.GrpcChannel(replica, ShopResources.Clients.Ordering));

    private static async Task SetStockAsync(HttpClient merchant, string sku, int onHand) =>
        (await merchant.PutAsJsonAsync($"/stock/{sku}", new { OnHand = onHand }))
            .StatusCode.Should()
            .Be(HttpStatusCode.NoContent);

    private static ReserveStockRequest Reserve(string orderId, string sku, int quantity) =>
        new()
        {
            OrderId = orderId,
            Lines =
            {
                new StockLine { Sku = sku, Quantity = quantity },
            },
        };

    private static Metadata Tenant(string tenant) =>
        new() { { WellKnownHeaders.TenantId, tenant } };

    private static string Unique() => $"SKU-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    private sealed record Level(string Sku, int OnHand, int Reserved, int Available);

    private sealed record JobRun(string Job, DateTimeOffset FireTime, string Replica, int Items);
}
