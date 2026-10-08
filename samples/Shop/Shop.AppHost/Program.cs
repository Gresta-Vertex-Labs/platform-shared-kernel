using Shop.AppHost;

// The Shop reference platform: every service of the system and every piece of infrastructure it talks to, local
// stand-ins only. `dotnet run --project samples/Shop/Shop.AppHost` opens the Aspire dashboard; Shop.E2E starts the
// same model with Aspire.Hosting.Testing.
var builder = DistributedApplication.CreateBuilder(args);

var pki = ShopPki.Ensure(ShopResources.Clients.Ordering, ShopResources.Clients.Rogue);
var infra = ShopInfrastructure.Add(builder, pki);

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
var inventories = new[] { ShopResources.Inventory, ShopResources.Inventory2 }
    .Select(name =>
        builder
            .AddProject<Projects.Shop_Inventory_Api>(name, launchProfileName: "https")
            .WithInventoryConfiguration(infra, pki, name)
            .WithHttpHealthCheck("/health/ready", endpointName: "http")
    )
    .ToList();

// The merchant's own system: it receives Billing's signed webhooks.
var merchant = builder
    .AddProject<Projects.Shop_Merchant_Api>(ShopResources.Merchant)
    .WithMerchantConfiguration()
    .WithHttpHealthCheck("/health/ready");

// Billing: payments for Ordering, invoices signed in Key Vault, webhooks to the merchant.
var billing = builder
    .AddProject<Projects.Shop_Billing_Api>(ShopResources.Billing)
    .WithBillingConfiguration(infra, pki, merchant)
    .WithHttpHealthCheck("/health/ready");

// Reports: sales exports (CSV, Excel, PDF) and statements (HTML to PDF), into S3 and OBS stores.
builder
    .AddProject<Projects.Shop_Reports_Api>(ShopResources.Reports)
    .WithReportsConfiguration(infra)
    .WithHttpHealthCheck("/health/ready");

// Notify: emails receipts and texts merchants, from Billing's events on RabbitMQ.
builder
    .AddProject<Projects.Shop_Notify_Worker>(ShopResources.Notify)
    .WithNotifyConfiguration(infra)
    .WithHttpHealthCheck("/health/ready");

// Ordering: one replica (its SignalR hub has no backplane), calling the first Inventory replica and Billing.
builder
    .AddProject<Projects.Shop_Ordering_Api>(ShopResources.Ordering)
    .WithOrderingConfiguration(infra, pki, inventories[0])
    .WithBillingClient(billing)
    .WithHttpHealthCheck("/health/ready");

await builder.Build().RunAsync();
