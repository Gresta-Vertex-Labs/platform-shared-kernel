using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Search.ElasticSearch.Tests.Support;

/// <summary>
/// The shared, fixed 15-document corpus used across every T-21–T-25 real-backend test — two tenants
/// (10 documents for TenantA, 5 for TenantB) across three categories.
/// </summary>
internal static class TestProductCorpus
{
    public static readonly TenantId TenantA = TestTenants.TenantA;
    public static readonly TenantId TenantB = TestTenants.TenantB;

    public static IReadOnlyList<TestProduct> All { get; } =
    [
        new() { DocumentId = "prod-001", TenantId = TenantA.ToString(), Name = "Wireless Mouse", Description = "A wireless ergonomic mouse with a long battery life", Status = "active", Category = "electronics", Price = 29.99, Stock = 150, InStock = true, CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-002", TenantId = TenantA.ToString(), Name = "Mechanical Keyboard", Description = "RGB mechanical keyboard with blue switches", Status = "active", Category = "electronics", Price = 89.99, Stock = 75, InStock = true, CreatedAt = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-003", TenantId = TenantA.ToString(), Name = "USB-C Hub", Description = "Seven port USB-C hub with HDMI output", Status = "active", Category = "electronics", Price = 49.99, Stock = 0, InStock = false, CreatedAt = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-004", TenantId = TenantA.ToString(), Name = "Standing Desk", Description = "Electric height adjustable standing desk", Status = "discontinued", Category = "furniture", Price = 399.99, Stock = 5, InStock = true, CreatedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-005", TenantId = TenantA.ToString(), Name = "Office Chair", Description = "Ergonomic mesh office chair with lumbar support", Status = "active", Category = "furniture", Price = 249.99, Stock = 20, InStock = true, CreatedAt = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-006", TenantId = TenantA.ToString(), Name = "Desk Lamp", Description = "LED desk lamp with adjustable brightness", Status = "active", Category = "furniture", Price = 34.99, Stock = 60, InStock = true, CreatedAt = new DateTimeOffset(2026, 1, 25, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-007", TenantId = TenantA.ToString(), Name = "Notebook Set", Description = "A set of three ruled notebooks", Status = "active", Category = "stationery", Price = 12.99, Stock = 200, InStock = true, CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-008", TenantId = TenantA.ToString(), Name = "Fountain Pen", Description = "Stainless steel fountain pen with a converter", Status = "discontinued", Category = "stationery", Price = 24.99, Stock = 0, InStock = false, CreatedAt = new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-009", TenantId = TenantA.ToString(), Name = "Sticky Notes", Description = "A pack of assorted colour sticky notes", Status = "active", Category = "stationery", Price = 4.99, Stock = 500, InStock = true, CreatedAt = new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-010", TenantId = TenantA.ToString(), Name = "Webcam", Description = "A 1080p webcam with a built in microphone", Status = "active", Category = "electronics", Price = 59.99, Stock = 40, InStock = true, CreatedAt = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-011", TenantId = TenantB.ToString(), Name = "Gaming Monitor", Description = "A 27 inch 144Hz gaming monitor", Status = "active", Category = "electronics", Price = 299.99, Stock = 30, InStock = true, CreatedAt = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-012", TenantId = TenantB.ToString(), Name = "Bluetooth Speaker", Description = "A portable waterproof bluetooth speaker", Status = "active", Category = "electronics", Price = 39.99, Stock = 90, InStock = true, CreatedAt = new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-013", TenantId = TenantB.ToString(), Name = "Bookshelf", Description = "A five tier wooden bookshelf", Status = "active", Category = "furniture", Price = 129.99, Stock = 15, InStock = true, CreatedAt = new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-014", TenantId = TenantB.ToString(), Name = "Whiteboard Markers", Description = "A pack of eight dry erase markers", Status = "active", Category = "stationery", Price = 9.99, Stock = 300, InStock = true, CreatedAt = new DateTimeOffset(2026, 1, 18, 0, 0, 0, TimeSpan.Zero) },
        new() { DocumentId = "prod-015", TenantId = TenantB.ToString(), Name = "Filing Cabinet", Description = "A two drawer steel filing cabinet", Status = "discontinued", Category = "furniture", Price = 179.99, Stock = 0, InStock = false, CreatedAt = new DateTimeOffset(2026, 1, 22, 0, 0, 0, TimeSpan.Zero) },
    ];

    public static IReadOnlyList<TestProduct> ForTenant(TenantId tenantId) => All.Where(p => p.TenantId == tenantId.ToString()).ToArray();
}
