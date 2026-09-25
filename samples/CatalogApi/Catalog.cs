using SharedKernel.Execution.Tenancy;

namespace CatalogApi;

/// <summary>The index names and the two tenants this sample serves.</summary>
public static class Catalog
{
    /// <summary>The Meilisearch storefront index.</summary>
    public const string ProductsIndex = "products";

    /// <summary>
    /// The ElasticSearch analytics index. Read and write alias are the SAME name here, which is the
    /// shape a service starts with: <c>EnsureIndexAsync</c> creates a concrete index of this name and
    /// both reads and writes address it directly. The read/write split on <c>AddIndex</c> exists for a
    /// rebuild, where <c>CutoverAsync</c> repoints the read alias at a freshly-built staging index
    /// while writes continue to the old one — it is not the starting configuration.
    /// </summary>
    public const string OrderLinesRead = "order-lines";

    /// <summary>The write alias. Same as <see cref="OrderLinesRead"/> until a rebuild is in flight.</summary>
    public const string OrderLinesWrite = "order-lines";

    /// <summary>The completion field <c>ISuggestSearch</c> answers from.</summary>
    public const string OrderLineSuggestField = OrderLineFields.ProductNameSuggest;

    /// <summary>The first tenant. Its string form is the value every one of its documents holds in the tenant field.</summary>
    public static readonly TenantId TenantNorth = new(Guid.Parse("6f1c2a4e-0b7d-4c3e-9a51-3d2e8f7b1a01"));

    /// <summary>The second tenant.</summary>
    public static readonly TenantId TenantSouth = new(Guid.Parse("6f1c2a4e-0b7d-4c3e-9a51-3d2e8f7b1a02"));
}

/// <summary>The fixed corpus this sample seeds, so every endpoint has known data to answer from.</summary>
/// <remarks>
/// Deliberately more than a handful of documents per tenant: the count-accuracy endpoint provisions a
/// low <c>MaxTotalHits</c> ceiling so a real corpus crosses it, which is the only way to see the
/// Meilisearch lower-bound behaviour without indexing thousands of rows.
/// </remarks>
public static class SeedData
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<ProductDocument> Products { get; } =
    [
        Product("prod-001", Catalog.TenantNorth, "Wireless Mouse", "A wireless ergonomic mouse with a long battery life", "Logitech", "electronics", 29.99, true, 4.5, 0),
        Product("prod-002", Catalog.TenantNorth, "Mechanical Keyboard", "RGB mechanical keyboard with blue switches", "Keychron", "electronics", 89.99, true, 4.7, 5),
        Product("prod-003", Catalog.TenantNorth, "USB-C Hub", "Seven port USB-C hub with HDMI output", "Anker", "electronics", 49.99, false, 4.1, 10),
        Product("prod-004", Catalog.TenantNorth, "Standing Desk", "Electric height adjustable standing desk", "Fully", "furniture", 399.99, true, 4.6, 15),
        Product("prod-005", Catalog.TenantNorth, "Office Chair", "Ergonomic mesh office chair with lumbar support", "Herman Miller", "furniture", 249.99, true, 4.8, 20),
        Product("prod-006", Catalog.TenantNorth, "Desk Lamp", "LED desk lamp with adjustable brightness", "BenQ", "furniture", 34.99, true, 4.2, 25),
        Product("prod-007", Catalog.TenantNorth, "Notebook Set", "A set of three ruled notebooks", "Leuchtturm", "stationery", 12.99, true, 4.4, 30),
        Product("prod-008", Catalog.TenantNorth, "Fountain Pen", "Stainless steel fountain pen with a converter", "Lamy", "stationery", 24.99, false, 4.9, 35),
        Product("prod-009", Catalog.TenantNorth, "Sticky Notes", "A pack of assorted colour sticky notes", "Post-it", "stationery", 4.99, true, 4.0, 40),
        Product("prod-010", Catalog.TenantNorth, "Webcam", "A 1080p webcam with a built in microphone", "Logitech", "electronics", 59.99, true, 3.9, 45),
        Product("prod-011", Catalog.TenantSouth, "Gaming Monitor", "A 27 inch 144Hz gaming monitor", "Dell", "electronics", 299.99, true, 4.6, 3),
        Product("prod-012", Catalog.TenantSouth, "Bluetooth Speaker", "A portable waterproof bluetooth speaker", "JBL", "electronics", 39.99, true, 4.3, 8),
        Product("prod-013", Catalog.TenantSouth, "Bookshelf", "A five tier wooden bookshelf", "IKEA", "furniture", 129.99, true, 4.1, 12),
        Product("prod-014", Catalog.TenantSouth, "Whiteboard Markers", "A pack of eight dry erase markers", "Expo", "stationery", 9.99, true, 4.2, 18),
        Product("prod-015", Catalog.TenantSouth, "Filing Cabinet", "A two drawer steel filing cabinet", "Bisley", "furniture", 179.99, false, 3.8, 22),
    ];

    public static IReadOnlyList<OrderLineDocument> OrderLines { get; } =
    [
        OrderLine("line-001", Catalog.TenantNorth, "Wireless Mouse", "electronics", "emea", 3, 89.97, 1),
        OrderLine("line-002", Catalog.TenantNorth, "Wireless Mouse", "electronics", "emea", 1, 29.99, 4),
        OrderLine("line-003", Catalog.TenantNorth, "Mechanical Keyboard", "electronics", "emea", 2, 179.98, 7),
        OrderLine("line-004", Catalog.TenantNorth, "Standing Desk", "furniture", "amer", 1, 399.99, 11),
        OrderLine("line-005", Catalog.TenantNorth, "Office Chair", "furniture", "amer", 4, 999.96, 14),
        OrderLine("line-006", Catalog.TenantNorth, "Notebook Set", "stationery", "apac", 10, 129.90, 19),
        OrderLine("line-007", Catalog.TenantNorth, "Sticky Notes", "stationery", "apac", 25, 124.75, 23),
        OrderLine("line-008", Catalog.TenantNorth, "Webcam", "electronics", "emea", 2, 119.98, 28),
        OrderLine("line-009", Catalog.TenantSouth, "Gaming Monitor", "electronics", "apac", 1, 299.99, 2),
        OrderLine("line-010", Catalog.TenantSouth, "Bluetooth Speaker", "electronics", "apac", 5, 199.95, 6),
        OrderLine("line-011", Catalog.TenantSouth, "Bookshelf", "furniture", "emea", 2, 259.98, 13),
        OrderLine("line-012", Catalog.TenantSouth, "Whiteboard Markers", "stationery", "emea", 12, 119.88, 21),
    ];

    private static ProductDocument Product(
        string id, TenantId tenant, string name, string description, string brand,
        string category, double price, bool inStock, double rating, int dayOffset) => new()
    {
        DocumentId = id,
        TenantId = tenant.ToString(),
        Name = name,
        Description = description,
        Brand = brand,
        Category = category,
        Price = price,
        InStock = inStock,
        Rating = rating,
        ReleasedOn = Epoch.AddDays(dayOffset),
    };

    private static OrderLineDocument OrderLine(
        string id, TenantId tenant, string productName, string category,
        string region, long quantity, double revenue, int dayOffset) => new()
    {
        DocumentId = id,
        TenantId = tenant.ToString(),
        ProductName = productName,
        ProductNameSuggest = productName,
        Category = category,
        Region = region,
        Quantity = quantity,
        Revenue = revenue,
        OrderedAt = Epoch.AddDays(dayOffset),
    };
}
