using Shop.AppHost;

// The Shop reference platform: every service of the system and every piece of infrastructure it talks to, local
// stand-ins only. `dotnet run --project samples/Shop/Shop.AppHost` opens the Aspire dashboard; Shop.E2E starts the
// same model with Aspire.Hosting.Testing.
var builder = DistributedApplication.CreateBuilder(args);

var infra = ShopInfrastructure.Add(builder);
var pki = ShopPki.Ensure(ShopResources.Clients.Ordering, ShopResources.Clients.Rogue);

// Catalog runs twice, so the end-to-end tests can prove what one replica does reaches the other (L2 cache backplane,
// Redis Pub/Sub) and that migrations and index provisioning are safe to run concurrently.
foreach (string name in new[] { ShopResources.Catalog, ShopResources.Catalog2 })
{
    builder
        .AddProject<Projects.Shop_Catalog_Api>(name)
        .WithCatalogConfiguration(infra)
        .WithHttpHealthCheck("/health/ready");
}

// Inventory runs twice too: its reconciliation job is scheduled on both, and must run once per occurrence.
foreach (string name in new[] { ShopResources.Inventory, ShopResources.Inventory2 })
{
    builder
        .AddProject<Projects.Shop_Inventory_Api>(name, launchProfileName: "https")
        .WithInventoryConfiguration(infra, pki, name)
        .WithHttpHealthCheck("/health/ready", endpointName: "http");
}

await builder.Build().RunAsync();
