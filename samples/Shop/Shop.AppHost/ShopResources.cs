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
    public const string KeyVault = "keyvault";
    public const string WireMock = "wiremock";

    public const string Catalog = "catalog-api";
    public const string Catalog2 = "catalog-api-2";
    public const string Inventory = "inventory-api";
    public const string Inventory2 = "inventory-api-2";
    public const string Ordering = "ordering-api";
    public const string Billing = "billing-api";
    public const string Merchant = "merchant-api";
    public const string Notify = "notify-worker";

    /// <summary>DEVELOPMENT-ONLY provider credentials; WireMock only accepts these (wiremock/mappings).</summary>
    public static class Providers
    {
        public const string SendGridApiKey = "SG.shop-dev-sendgrid-key";
        public const string TwilioAccountSid = "AC00000000000000000000000000000000";
        public const string TwilioAuthToken = "shop-dev-twilio-token";
        public const string TwilioFrom = "+15005550006";

        /// <summary>The Contoso merchant's phone (Notify texts it on every paid order).</summary>
        public const string ContosoMerchantPhone = "+31201234567";
    }

    /// <summary>
    /// Billing's DEVELOPMENT-ONLY API keys, minted with SharedKernel.Security.ApiKey's generator (prefix <c>shop_dev</c>).
    /// Billing knows them only by key id and hash; the hash is provisioned into Key Vault.
    /// </summary>
    public static class ApiKeys
    {
        /// <summary>The Ordering service: charges and refunds, for any tenant.</summary>
        public const string OrderingService =
            "shop_dev_X7UObhVO08jo8ioj_s2v1GezzxFvvhWukTJaLV3cfXFtz3Zn32wntAE";

        /// <summary>The payment provider's callbacks for the Contoso merchant.</summary>
        public const string PaymentProvider =
            "shop_dev_YvP7MCgN3pJZnM6K_Cw6ZxzD1cSd9t6OljCXbbuocsJP6HKE14RiLgn";

        /// <summary>Every key by its key id (the 16 characters after the prefix).</summary>
        public static readonly (string KeyId, string Key)[] All =
        [
            ("X7UObhVO08jo8ioj", OrderingService),
            ("YvP7MCgN3pJZnM6K", PaymentProvider),
        ];
    }

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
