using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Streaming;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Security.Mtls;
using SharedKernel.Testing.Caching;
using SharedKernel.Testing.Grpc;
using SharedKernel.Testing.Scheduling;
using SharedKernel.Testing.Security;
using Shop.Contracts.Inventory;
using Shop.Inventory.Api.Reconciliation;
using Shop.Inventory.Api.Security;
using Shop.Inventory.Api.Stock;
using Shop.TestSupport;
using Xunit;

namespace Shop.Inventory.Tests;

public sealed class InventoryArchitectureTests
{
    private static readonly DependencyGraph Graph = DependencyGraph.Load("Shop.Inventory.Tests");
    private const string Service = "Shop.Inventory.Api";

    [Fact]
    public void Inventory_ReferencesOnlyTheSharedContracts_NoOtherService() =>
        Graph.DirectProjects(Service).Should().BeEquivalentTo(["Shop.Contracts"]);

    [Fact]
    public void Inventory_NeverReferencesTestingPackages() =>
        Graph
            .Closure(Service)
            .Where(p => KernelPackageIndex.Instance.TierOf(p) == "Testing")
            .Should()
            .BeEmpty();

    [Fact]
    public void EveryKernelPackage_IsKnown() =>
        Graph
            .Closure(Service)
            .Where(p => p.StartsWith("SharedKernel.", StringComparison.Ordinal))
            .Should()
            .OnlyContain(p => KernelPackageIndex.Instance.TierOf(p) != "ThirdParty");
}

public sealed class ServiceHeaderTenantResolutionStrategyTests
{
    private static readonly TenantId Tenant = new(
        Guid.Parse("b2f4a6c8-1d3e-4a5b-8c7d-9e0f1a2b3c4d")
    );
    private readonly ServiceHeaderTenantResolutionStrategy _strategy = new();

    [Fact]
    public async Task CertificateCaller_WithTheHeader_GetsThatTenant() =>
        (
            await _strategy.TryResolveAsync(
                Context(MtlsAuthenticationDefaults.AuthenticationScheme, Tenant.ToString()),
                CancellationToken.None
            )
        )
            .Should()
            .Be(Tenant);

    [Fact]
    public async Task TokenCaller_WithTheHeader_GetsNoTenantFromIt() =>
        (
            await _strategy.TryResolveAsync(
                Context("Bearer", Tenant.ToString()),
                CancellationToken.None
            )
        )
            .Should()
            .BeNull(
                "only a service authenticated by its certificate may name a tenant in a header"
            );

    [Fact]
    public async Task Anonymous_WithTheHeader_GetsNoTenant() =>
        (
            await _strategy.TryResolveAsync(
                Context(authenticationType: null, Tenant.ToString()),
                CancellationToken.None
            )
        )
            .Should()
            .BeNull();

    [Fact]
    public async Task CertificateCaller_WithAMalformedHeader_GetsNoTenant() =>
        (
            await _strategy.TryResolveAsync(
                Context(MtlsAuthenticationDefaults.AuthenticationScheme, "not-a-guid"),
                CancellationToken.None
            )
        )
            .Should()
            .BeNull();

    private static DefaultHttpContext Context(string? authenticationType, string? tenantHeader)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType)),
        };
        if (tenantHeader is not null)
        {
            context.Request.Headers[WellKnownHeaders.TenantId] = tenantHeader;
        }

        return context;
    }
}

public sealed class ShopServiceCertificateValidatorTests
{
    [Fact]
    public async Task AllowListedCertificate_IsTheNamedService_WithTheReservePermission()
    {
        var certificate = new MtlsTestCertificateBuilder().AsChainedFromEphemeralCa().Build();
        var validator = Validator(
            certificate.Certificate.GetCertHashString(HashAlgorithmName.SHA256),
            "ordering-api"
        );

        var result = await validator.ValidateAsync(certificate.Certificate, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.ClientId.Should().Be("ordering-api");
        result.Permissions.Should().BeEquivalentTo([InventoryPermissions.Reserve]);
    }

    [Fact]
    public async Task CertificateNotOnTheAllowList_IsRejected()
    {
        var certificate = new MtlsTestCertificateBuilder().AsChainedFromEphemeralCa().Build();
        var validator = Validator(new string('A', 64), "ordering-api");

        var result = await validator.ValidateAsync(certificate.Certificate, CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    private static ShopServiceCertificateValidator Validator(string thumbprint, string client) =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [$"Inventory:Mtls:Clients:{thumbprint}"] = client,
                    }
                )
                .Build()
        );
}

public sealed class SkuLocksTests
{
    private static readonly TenantId Tenant = new(
        Guid.Parse("6c1d7e1a-3b52-4f8e-9a41-2f6b8c0d9e11")
    );

    [Fact]
    public async Task OneSkuOfTheOrderIsHeld_NothingIsAcquired_AndWhatWasTakenIsReleased()
    {
        var locks = new FakeDistributedLockService();
        var skuLocks = new SkuLocks(locks);
        await using var other = await skuLocks.AcquireAsync(Tenant, ["B"], CancellationToken.None);

        var attempt = await skuLocks.AcquireAsync(Tenant, ["A", "B"], CancellationToken.None);

        attempt.Should().BeNull("the order cannot have every SKU it needs");
        await using var a = await skuLocks.AcquireAsync(Tenant, ["A"], CancellationToken.None);
        a.Should().NotBeNull("the lock on A taken before B failed was released");
    }

    [Fact]
    public async Task SameSkuInTwoTenants_DoesNotContend()
    {
        var skuLocks = new SkuLocks(new FakeDistributedLockService());
        await using var contoso = await skuLocks.AcquireAsync(
            Tenant,
            ["A"],
            CancellationToken.None
        );

        await using var fabrikam = await skuLocks.AcquireAsync(
            new TenantId(Guid.Parse("b2f4a6c8-1d3e-4a5b-8c7d-9e0f1a2b3c4d")),
            ["A"],
            CancellationToken.None
        );

        fabrikam.Should().NotBeNull();
    }
}

public sealed class InventoryJobsTests
{
    [Fact]
    public async Task Reconciliation_SendsItsCommand_WithTheFireTime()
    {
        var sender = new RecordingSender();
        var registry = new InMemoryScheduledJobRegistry(sender);
        InventoryJobs.Register(registry, reconciliationCron: null);
        var at = new DateTimeOffset(2026, 10, 6, 12, 0, 2, TimeSpan.Zero);

        var outcome = await registry.TriggerAsync(ReconcileStockHandler.JobName, at);

        outcome!.Value.IsSuccess.Should().BeTrue();
        sender.Sent.Should().ContainSingle().Which.Should().Be(new ReconcileStockCommand(at));
    }

    private sealed class RecordingSender : ISender
    {
        public List<object> Sent { get; } = [];

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default
        )
        {
            Sent.Add(request);
            return Task.FromResult((TResponse)(object)Result.Success());
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamQuery<TResponse> request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}

/// <summary>
/// The gRPC service over a real <c>ServerCallContext</c> (Presentation.Testing's <c>TestServerCallContext</c>): what it
/// sends for a reservation, and that a refusal leaves as an exception for the kernel's interceptor to map to a status.
/// </summary>
public sealed class InventoryGrpcServiceTests
{
    [Fact]
    public async Task ReserveStock_SendsTheOrdersLines_AndAnswersWithTheReservation()
    {
        var reservation = Guid.NewGuid();
        var sender = new AnsweringSender(Result<Guid>.Success(reservation));
        var orderId = Guid.NewGuid();
        var request = new ReserveStockRequest { OrderId = orderId.ToString("D") };
        request.Lines.Add(new StockLine { Sku = "SKU-1", Quantity = 2 });

        var reply = await new Shop.Inventory.Api.InventoryGrpcService(sender).ReserveStock(
            request,
            TestServerCallContext.Create(correlationId: "corr-1")
        );

        reply.ReservationId.Should().Be(reservation.ToString("D"));
        var sent = sender
            .Sent.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<ReserveStockCommand>()
            .Subject;
        sent.OrderId.Should().Be(orderId);
        sent.Lines.Should().Equal(new ReservationLine("SKU-1", 2));
    }

    [Fact]
    public async Task ReserveStock_Refused_ThrowsForTheInterceptor()
    {
        var sender = new AnsweringSender(
            Result<Guid>.Failure(Error.BusinessRule("inventory.insufficient_stock", "Not enough."))
        );
        var request = new ReserveStockRequest { OrderId = Guid.NewGuid().ToString("D") };
        request.Lines.Add(new StockLine { Sku = "SKU-1", Quantity = 9 });

        var reserve = () =>
            new Shop.Inventory.Api.InventoryGrpcService(sender).ReserveStock(
                request,
                TestServerCallContext.Create()
            );

        (await reserve.Should().ThrowAsync<Exception>())
            .Which.Message.Should()
            .Contain("Not enough");
    }

    private sealed class AnsweringSender(object answer) : ISender
    {
        public List<object> Sent { get; } = [];

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default
        )
        {
            Sent.Add(request);
            return Task.FromResult((TResponse)answer);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamQuery<TResponse> request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
