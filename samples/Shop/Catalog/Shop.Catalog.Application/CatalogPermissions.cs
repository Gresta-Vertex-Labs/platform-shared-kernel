namespace Shop.Catalog.Application;

/// <summary>The permissions the catalog's use cases require; issued by the identity provider as token claims.</summary>
public static class CatalogPermissions
{
    /// <summary>Browse and search the caller's catalog.</summary>
    public const string Read = "catalog.read";

    /// <summary>Create products, change prices, upload images, use the back office.</summary>
    public const string Manage = "catalog.manage";
}
