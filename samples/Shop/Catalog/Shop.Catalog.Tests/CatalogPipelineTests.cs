using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Application.Pipeline.Caching;
using SharedKernel.Domain.Monetary;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Testing;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Storage;
using SharedKernel.Testing.Application;
using SharedKernel.Testing.Caching;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Execution;
using SharedKernel.Testing.FeatureManagement;
using SharedKernel.Testing.Intelligence;
using SharedKernel.Testing.Search;
using SharedKernel.Testing.Storage;
using SharedKernel.Validation.FluentValidation;
using Shop.Catalog.Application;
using Shop.Catalog.Application.Images;
using Shop.Catalog.Application.Products;
using Shop.Catalog.Application.Search;
using Shop.Catalog.Domain;
using Shop.Catalog.Infrastructure.Search;
using Xunit;

namespace Shop.Catalog.Tests;

/// <summary>The Catalog's use cases through the real kernel pipeline (validation, authorization, transactions, caching) over the kernel's fakes.</summary>
public sealed class CatalogPipelineTests : IDisposable
{
    private static readonly TenantId Contoso = new(
        Guid.Parse("6c1d7e1a-3b52-4f8e-9a41-2f6b8c0d9e11")
    );
    private static readonly TenantId Fabrikam = new(
        Guid.Parse("b2f4a6c8-1d3e-4a5b-8c7d-9e0f1a2b3c4d")
    );
    private const string EmbeddingModel = "test-embedding";

    private readonly FakeClock _clock = new();
    private readonly RecordingBroadcaster _broadcaster = new();
    private readonly List<ApplicationPipelineTestHarness> _harnesses = [];

    [Fact]
    public async Task CreateProduct_IndexesItInTheStorefront_TheBackOffice_AndTheVectorCollection()
    {
        var harness = Build(Merchant(Contoso));

        var created = await harness.SendAsync(Keyboard());

        created.IsSuccess.Should().BeTrue();
        string id = created.Value.ToString("D");
        Service<InMemorySearchIndex<ProductDocument>>(harness).WasIndexed(id).Should().BeTrue();
        Service<InMemorySearchIndex<ProductAdminDocument>>(harness)
            .WasIndexed(id)
            .Should()
            .BeTrue();
        Service<InMemoryVectorCollection<ProductVector>>(harness).WasUpserted(id).Should().BeTrue();
    }

    [Fact]
    public async Task CreateProduct_WithoutTheManagePermission_IsForbidden_AndNothingIsWritten()
    {
        var harness = Build(
            TestRequestContext.ForTenant(Contoso).WithPermissions(CatalogPermissions.Read)
        );

        var created = await harness.SendAsync(Keyboard());

        created.IsFailure.Should().BeTrue();
        created.Error.Type.Should().Be(ErrorType.Forbidden);
        Service<FakeRepository<Product, ProductId>>(harness).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateProduct_ForACallerWithoutATenant_FailsClosed()
    {
        var harness = Build(
            TestRequestContext.ForUser().WithPermissions(CatalogPermissions.Manage)
        );

        var created = await harness.SendAsync(Keyboard());

        created.IsFailure.Should().BeTrue();
        created.Error.Code.Should().Be("catalog.tenant_required");
    }

    [Fact]
    public async Task CreateProduct_WithAnInvalidRequest_FailsValidation_BeforeTheHandler()
    {
        var harness = Build(Merchant(Contoso));

        var created = await harness.SendAsync(Keyboard() with { Price = 0m, Currency = "EURO" });

        created.IsFailure.Should().BeTrue();
        created.Error.Type.Should().Be(ErrorType.Validation);
        Service<FakeRepository<Product, ProductId>>(harness).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateProduct_WithATakenSku_IsAConflict()
    {
        var harness = Build(Merchant(Contoso));
        (await harness.SendAsync(Keyboard())).IsSuccess.Should().BeTrue();

        var again = await harness.SendAsync(Keyboard() with { Name = "Another keyboard" });

        again.IsFailure.Should().BeTrue();
        again.Error.Type.Should().Be(ErrorType.Conflict);
        again.Error.Code.Should().Be("catalog.product.sku_taken");
    }

    [Fact]
    public async Task GetProduct_IsServedFromTheCache_UntilAPriceChangeEvictsIt()
    {
        var harness = Build(Merchant(Contoso));
        Guid id = (await harness.SendAsync(Keyboard())).Value;

        var first = await harness.SendAsync(new GetProductQuery(id));
        _clock.Advance(TimeSpan.FromSeconds(5));
        var cached = await harness.SendAsync(new GetProductQuery(id));

        cached.Value.ServedAt.Should().Be(first.Value.ServedAt, "the second read is a cache hit");

        (await harness.SendAsync(new ChangePriceCommand(id, 59.90m, "EUR")))
            .IsSuccess.Should()
            .BeTrue();
        _clock.Advance(TimeSpan.FromSeconds(5));
        var fresh = await harness.SendAsync(new GetProductQuery(id));

        fresh.Value.Price.Should().Be(59.90m);
        fresh
            .Value.ServedAt.Should()
            .BeAfter(first.Value.ServedAt, "the price change evicted the cached read");
    }

    [Fact]
    public async Task GetProduct_CarriesTheBadge_OnlyWhereTheFlagIsOn()
    {
        var on = Build(Merchant(Contoso), badge: true);
        var off = Build(Merchant(Contoso), badge: false);

        Guid onId = (await on.SendAsync(Keyboard())).Value;
        Guid offId = (await off.SendAsync(Keyboard())).Value;

        (await on.SendAsync(new GetProductQuery(onId))).Value.Badge.Should().Be("new");
        (await off.SendAsync(new GetProductQuery(offId))).Value.Badge.Should().BeNull();
    }

    [Fact]
    public async Task SearchProducts_SeesOnlyTheCallersTenant()
    {
        var contoso = Build(Merchant(Contoso));
        (await contoso.SendAsync(Keyboard())).IsSuccess.Should().BeTrue();
        var index = Service<InMemorySearchIndex<ProductDocument>>(contoso);

        var fabrikam = Build(Merchant(Fabrikam), storefront: index);
        var found = await fabrikam.SendAsync(new SearchProductsQuery("keyboard", null));

        found.IsSuccess.Should().BeTrue();
        found.Value.Hits.Should().BeEmpty("Fabrikam must never see Contoso's products");
    }

    [Fact]
    public async Task SemanticSearch_FindsTheProductByItsDescription()
    {
        var harness = Build(Merchant(Contoso));
        Guid id = (await harness.SendAsync(Keyboard())).Value;

        var hits = await harness.SendAsync(
            new SemanticSearchQuery(
                "Mechanical keyboard. Peripherals. Hot-swappable switches, aluminium case."
            )
        );

        hits.IsSuccess.Should().BeTrue();
        hits.Value.Should().NotBeEmpty();
        hits.Value[0].Id.Should().Be(id);
    }

    [Fact]
    public async Task UploadImage_StoresItUnderTheCallersTenant_AndGivesADownloadUrl()
    {
        var harness = Build(Merchant(Contoso));
        Guid id = (await harness.SendAsync(Keyboard())).Value;

        using var png = new MemoryStream([0x89, 0x50, 0x4E, 0x47]);
        var uploaded = await harness.SendAsync(new UploadProductImageCommand(id, png, "image/png"));
        var url = await harness.SendAsync(new GetProductImageUrlQuery(id));

        uploaded.IsSuccess.Should().BeTrue();
        url.IsSuccess.Should().BeTrue(url.IsFailure ? url.Error.Message : "the image exists");
        (await harness.SendAsync(new GetProductQuery(id))).Value.HasImage.Should().BeTrue();
    }

    [Fact]
    public async Task PriceChangedEvent_IsBroadcast()
    {
        var handler = new BroadcastPriceChange(_broadcaster);
        var productId = ProductId.New();

        await handler.Handle(
            new ProductPriceChanged(
                productId,
                Contoso,
                Money.Create(10m, Currency.Eur).Value,
                Money.Create(12m, Currency.Eur).Value
            )
            {
                OccurredOn = _clock.UtcNow,
            },
            CancellationToken.None
        );

        _broadcaster
            .Published.Should()
            .ContainSingle(change => change.ProductId == productId.Value && change.Price == 12m);
    }

    public void Dispose()
    {
        foreach (var harness in _harnesses)
        {
            harness.Dispose();
        }
    }

    private static CreateProductCommand Keyboard() =>
        new(
            "KB-001",
            "Mechanical keyboard",
            "Hot-swappable switches, aluminium case.",
            "Keychron",
            "Peripherals",
            49.90m,
            "EUR"
        );

    private static TestRequestContext Merchant(TenantId tenant) =>
        TestRequestContext
            .ForTenant(tenant)
            .WithPermissions(CatalogPermissions.Read, CatalogPermissions.Manage);

    // The harness does not expose its provider; the kernel's Add* helpers register each fake as an instance.
    private static T Service<T>(ApplicationPipelineTestHarness harness)
        where T : class =>
        (T)
            harness
                .Services.Last(d =>
                    d.ServiceType == typeof(T) && d.ImplementationInstance is not null
                )
                .ImplementationInstance!;

    private ApplicationPipelineTestHarness Build(
        IRequestContext caller,
        bool badge = true,
        InMemorySearchIndex<ProductDocument>? storefront = null
    )
    {
        var harness = new ApplicationPipelineTestHarness();
        _harnesses.Add(harness);
        var services = harness.Services;

        services.AddSingleton(caller);
        services.AddSingleton<IClock>(_clock);
        services.AddFakeUnitOfWork();
        services.AddFakeRepository<Product, ProductId>();
        services.AddFakeCachingServices();
        services.AddFakeFeatureFlags(flags =>
            flags.SetEnabled(CatalogFeatures.NewArrivalBadge, badge)
        );

        if (storefront is null)
        {
            services.AddInMemorySearchIndex<ProductDocument>(
                CatalogDefinitions.Build(CatalogIndexes.Storefront, CatalogDefinitions.Storefront)
            );
        }
        else
        {
            services.AddSingleton(storefront);
            services.AddSingleton<SharedKernel.Search.Abstractions.Abstractions.ISearchIndex<ProductDocument>>(
                storefront
            );
        }

        services.AddInMemorySearchIndex<ProductAdminDocument>(
            CatalogDefinitions.Build(CatalogIndexes.BackOffice, CatalogDefinitions.BackOffice)
        );
        services.AddInMemoryEmbeddingGenerator(EmbeddingModel, 16);
        services.AddInMemoryVectorCollection<ProductVector>(
            CatalogDefinitions.Build(
                CatalogIndexes.Vectors,
                CatalogDefinitions.Vectors(EmbeddingModel, 16)
            )
        );
        services.AddSharedKernelStorage().AddInMemoryTenantStore(CatalogIndexes.ImageStore);
        services.AddSingleton<IPriceChangeBroadcaster>(_broadcaster);
        services.AddScoped<ProductIndexer>();
        services.AddFluentValidationRequestValidators(typeof(CreateProductCommand).Assembly);

        harness
            .Configure(app => app.WithTransactions().WithCaching())
            .Build<CreateProductCommand>();
        return harness;
    }

    private sealed class RecordingBroadcaster : IPriceChangeBroadcaster
    {
        public List<PriceChange> Published { get; } = [];

        public Task PublishAsync(PriceChange change, CancellationToken cancellationToken)
        {
            Published.Add(change);
            return Task.CompletedTask;
        }
    }
}
