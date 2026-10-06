using Shop.AppHost;

// The Shop reference platform: every service of the system and every piece of infrastructure it talks to, local
// stand-ins only. `dotnet run --project samples/Shop/Shop.AppHost` opens the Aspire dashboard; Shop.E2E starts the
// same model with Aspire.Hosting.Testing.
var builder = DistributedApplication.CreateBuilder(args);

var infra = ShopInfrastructure.Add(builder);

// Catalog runs twice, so the end-to-end tests can prove what one replica does reaches the other (L2 cache backplane,
// Redis Pub/Sub) and that migrations and index provisioning are safe to run concurrently.
foreach (string name in new[] { ShopResources.Catalog, ShopResources.Catalog2 })
{
    builder
        .AddProject<Projects.Shop_Catalog_Api>(name)
        .WithCatalogConfiguration(infra)
        .WithHttpHealthCheck("/health/ready");
}

await builder.Build().RunAsync();
