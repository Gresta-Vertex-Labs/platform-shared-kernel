namespace Shop.AppHost;

/// <summary>Resource names, shared with Shop.E2E.</summary>
public static class ShopResources
{
    public const string Postgres = "postgres";
    public const string Redis = "redis";
    public const string Meilisearch = "meilisearch";
    public const string Elasticsearch = "elasticsearch";
    public const string Qdrant = "qdrant";
    public const string Ollama = "ollama";
    public const string Minio = "minio";
    public const string Keycloak = "keycloak";
    public const string RabbitMq = "rabbitmq";
    public const string Temporal = "temporal";

    public const string Catalog = "catalog-api";
    public const string Catalog2 = "catalog-api-2";
    public const string Inventory = "inventory-api";
    public const string Inventory2 = "inventory-api-2";
    public const string Ordering = "ordering-api";

    /// <summary>The clients of the Shop's development PKI (mutual TLS between services).</summary>
    public static class Clients
    {
        public const string Ordering = "ordering-api";

        /// <summary>Signed by the Shop's CA but on no allow-list: proves the allow-list, not just the chain, decides.</summary>
        public const string Rogue = "rogue-service";
    }

    /// <summary>The Keycloak realm, client and test users (see keycloak/shop-realm.json).</summary>
    public static class Identity
    {
        public const string Realm = "shop";
        public const string Client = "shop-web";
        public const string Password = "Shop-dev-1!";
        public const string ContosoMerchant = "alice";
        public const string FabrikamMerchant = "bruno";
        public const string ContosoCustomer = "carol";
        public const string ContosoTenant = "6c1d7e1a-3b52-4f8e-9a41-2f6b8c0d9e11";
        public const string FabrikamTenant = "b2f4a6c8-1d3e-4a5b-8c7d-9e0f1a2b3c4d";
    }
}
