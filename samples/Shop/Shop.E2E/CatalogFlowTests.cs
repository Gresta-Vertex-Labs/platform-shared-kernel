using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Shop.AppHost;
using Shop.E2E.Infrastructure;
using Xunit;

namespace Shop.E2E;

/// <summary>
/// Flow 1, browse: Keycloak sign-in, the catalog over REST and GraphQL on two replicas, the two-level cache and its
/// backplane, Redis Pub/Sub, three search engines, the chat model, the image store, tenancy, permissions, localization.
/// </summary>
[Collection(ShopPlatformCollection.Name)]
public sealed class CatalogFlowTests(ShopPlatform platform)
{
    private const string Alice = ShopResources.Identity.ContosoMerchant;
    private const string Bruno = ShopResources.Identity.FabrikamMerchant;
    private const string Carol = ShopResources.Identity.ContosoCustomer;

    [E2EFact]
    public async Task AnonymousCaller_IsRejected()
    {
        using var anonymous = await platform.ClientAsync(ShopResources.Catalog);

        var response = await anonymous.GetAsync($"/products/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [E2EFact]
    public async Task Merchant_CreatesAProduct_AndReadsItBack_WithTheTenantsBadge()
    {
        using var alice = await platform.ClientAsync(ShopResources.Catalog, Alice);
        var id = await CreateAsync(
            alice,
            Unique("KB"),
            "Mechanical keyboard",
            "Hot-swappable switches, aluminium case."
        );

        var product = await alice.GetFromJsonAsync<Product>($"/products/{id}");

        product!.Name.Should().Be("Mechanical keyboard");
        product.Badge.Should().Be("new", "the NewArrivalBadge flag targets Contoso's tenant group");
    }

    [E2EFact]
    public async Task CustomerWithoutTheManagePermission_CannotCreateProducts()
    {
        using var carol = await platform.ClientAsync(ShopResources.Catalog, Carol);

        var response = await carol.PostAsJsonAsync(
            "/products",
            NewProduct(Unique("NO"), "Forbidden", "Not allowed.")
        );

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [E2EFact]
    public async Task AnotherTenant_CannotSeeTheProduct_AndHasNoBadge()
    {
        using var alice = await platform.ClientAsync(ShopResources.Catalog, Alice);
        using var bruno = await platform.ClientAsync(ShopResources.Catalog, Bruno);
        var contosoProduct = await CreateAsync(alice, Unique("CT"), "Contoso lamp", "A desk lamp.");
        var fabrikamProduct = await CreateAsync(
            bruno,
            Unique("FB"),
            "Fabrikam lamp",
            "A floor lamp."
        );

        (await bruno.GetAsync($"/products/{contosoProduct}"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
        (await bruno.GetFromJsonAsync<Product>($"/products/{fabrikamProduct}"))!
            .Badge.Should()
            .BeNull();
    }

    [E2EFact]
    public async Task ReadIsCached_AcrossReplicas_AndAPriceChangeOnOneReplica_EvictsItOnTheOther()
    {
        using var replica1 = await platform.ClientAsync(ShopResources.Catalog, Alice);
        using var replica2 = await platform.ClientAsync(ShopResources.Catalog2, Alice);
        var id = await CreateAsync(replica1, Unique("CA"), "Cached mug", "A mug.");

        var first = await replica1.GetFromJsonAsync<Product>($"/products/{id}");
        var fromOtherReplica = await replica2.GetFromJsonAsync<Product>($"/products/{id}");
        fromOtherReplica!
            .ServedAt.Should()
            .Be(first!.ServedAt, "replica 2 reads replica 1's entry from the Redis L2 cache");

        (
            await replica1.PutAsJsonAsync(
                $"/products/{id}/price",
                new { Price = 21.50m, Currency = "EUR" }
            )
        )
            .StatusCode.Should()
            .Be(HttpStatusCode.NoContent);

        await Eventually(
            async () =>
            {
                var after = await replica2.GetFromJsonAsync<Product>($"/products/{id}");
                return after!.Price == 21.50m;
            },
            "the backplane evicts replica 2's L1 copy"
        );

        await Eventually(
            async () =>
            {
                var heard = await replica2.GetFromJsonAsync<List<PriceChange>>(
                    "/ops/price-changes"
                );
                return heard!.Any(change => change.ProductId == id && change.Price == 21.50m);
            },
            "replica 2 hears the change on Redis Pub/Sub"
        );
    }

    [E2EFact]
    public async Task StorefrontSearch_FindsByWord_AndBySynonym()
    {
        using var alice = await platform.ClientAsync(ShopResources.Catalog, Alice);
        string sku = Unique("NB");
        var id = await CreateAsync(
            alice,
            sku,
            $"Ultralight notebook {sku}",
            "A 13-inch notebook computer.",
            category: "Computers"
        );

        var byWord = await alice.GetFromJsonAsync<SearchPage>($"/search?q={sku}");
        var bySynonym = await alice.GetFromJsonAsync<SearchPage>(
            $"/search?q=laptop {sku}&category=Computers"
        );

        byWord!.Hits.Should().Contain(hit => hit.Id == id);
        bySynonym!
            .Hits.Should()
            .Contain(hit => hit.Id == id, "the index declares laptop => notebook");
    }

    [E2EFact]
    public async Task BackOfficeSearch_CountsProductsPerBrand()
    {
        using var alice = await platform.ClientAsync(ShopResources.Catalog, Alice);
        string brand = Unique("BRAND");
        await CreateAsync(alice, Unique("B1"), "Brand item one", "First.", brand: brand);
        await CreateAsync(alice, Unique("B2"), "Brand item two", "Second.", brand: brand);

        var page = await alice.GetFromJsonAsync<BackOfficePage>("/search/back-office?q=Brand item");

        page!.Brands.Should().ContainKey(brand).WhoseValue.Should().BeGreaterThanOrEqualTo(2);
    }

    [E2EFact]
    public async Task SemanticSearch_FindsAProductByMeaning()
    {
        using var alice = await platform.ClientAsync(ShopResources.Catalog, Alice);
        var id = await CreateAsync(
            alice,
            Unique("SM"),
            "Espresso machine",
            "Brews strong Italian coffee with a 15-bar pump.",
            category: "Kitchen"
        );

        var hits = await alice.GetFromJsonAsync<List<SemanticHit>>(
            "/search/semantic?q=" + Uri.EscapeDataString("something to make coffee in the morning")
        );

        hits!.Select(hit => hit.Id).Should().Contain(id);
    }

    [E2EFact]
    public async Task ChatModel_SuggestsADescription()
    {
        using var alice = await platform.ClientAsync(ShopResources.Catalog, Alice);
        var id = await CreateAsync(
            alice,
            Unique("AI"),
            "Trail running shoes",
            "Grippy soles.",
            category: "Sport"
        );

        using var response = await alice.PostAsync(
            $"/products/{id}/description-suggestion",
            content: null
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var suggestion = await response.Content.ReadFromJsonAsync<Suggestion>();
        suggestion!.Text.Should().NotBeNullOrWhiteSpace();
        suggestion.CompletionTokens.Should().BeGreaterThan(0);
    }

    [E2EFact]
    public async Task ProductImage_IsUploaded_AndDownloadedThroughAPresignedUrl()
    {
        using var alice = await platform.ClientAsync(ShopResources.Catalog, Alice);
        var id = await CreateAsync(alice, Unique("IM"), "Framed print", "A print.");
        byte[] image = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

        using var body = new ByteArrayContent(image);
        body.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        (await alice.PutAsync($"/products/{id}/image", body))
            .StatusCode.Should()
            .Be(HttpStatusCode.NoContent);

        var url = await alice.GetFromJsonAsync<ImageUrl>($"/products/{id}/image-url");
        using var direct = new HttpClient();
        (await direct.GetByteArrayAsync(url!.Url)).Should().Equal(image);
    }

    [E2EFact]
    public async Task GraphQL_ServesTheSameProduct()
    {
        using var alice = await platform.ClientAsync(ShopResources.Catalog, Alice);
        var id = await CreateAsync(alice, Unique("GQ"), "GraphQL poster", "A poster.");

        using var response = await alice.PostAsJsonAsync(
            "/graphql",
            new { query = $"{{ product(id: \"{id}\") {{ name sku badge }} }}" }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("data")
            .GetProperty("product")
            .GetProperty("name")
            .GetString()
            .Should()
            .Be("GraphQL poster");
    }

    [E2EFact]
    public async Task Problems_AreLocalized_ToTheRequestCulture()
    {
        using var turkish = await platform.ClientAsync(
            ShopResources.Catalog,
            Alice,
            culture: "tr-TR"
        );
        var missing = Guid.NewGuid();

        using var response = await turkish.GetAsync($"/products/{missing}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("detail")
            .GetString()
            .Should()
            .Be($"{missing} numaralı ürün bulunamadı.");
        json.RootElement.GetProperty("errorCode")
            .GetString()
            .Should()
            .Be("catalog.product.not_found");
    }

    [E2EFact]
    public async Task OpenApiDocument_IsServedInDevelopment()
    {
        using var anonymous = await platform.ClientAsync(ShopResources.Catalog);

        var response = await anonymous.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<Guid> CreateAsync(
        HttpClient client,
        string sku,
        string name,
        string description,
        string category = "Home",
        string brand = "Shop"
    )
    {
        using var response = await client.PostAsJsonAsync(
            "/products",
            NewProduct(sku, name, description, category, brand)
        );
        response
            .StatusCode.Should()
            .Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static object NewProduct(
        string sku,
        string name,
        string description,
        string category = "Home",
        string brand = "Shop"
    ) =>
        new
        {
            Sku = sku,
            Name = name,
            Description = description,
            Brand = brand,
            Category = category,
            Price = 19.90m,
            Currency = "EUR",
        };

    private static string Unique(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    private static async Task Eventually(Func<Task<bool>> condition, string because)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!await condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException($"Timed out waiting until {because}.");
            }

            await Task.Delay(250);
        }
    }

    private sealed record Created(Guid Id);

    private sealed record Product(
        Guid Id,
        string Sku,
        string Name,
        decimal Price,
        string Currency,
        bool HasImage,
        string? Badge,
        DateTimeOffset ServedAt
    );

    private sealed record PriceChange(
        Guid ProductId,
        Guid TenantId,
        decimal Price,
        string Currency
    );

    private sealed record Hit(Guid Id, string Sku, string Name);

    private sealed record SearchPage(List<Hit> Hits, long TotalHits);

    private sealed record BackOfficePage(List<Hit> Hits, Dictionary<string, long> Brands);

    private sealed record SemanticHit(Guid Id, string Name, float Score);

    private sealed record Suggestion(string Text, int PromptTokens, int CompletionTokens);

    private sealed record ImageUrl(Uri Url);
}
