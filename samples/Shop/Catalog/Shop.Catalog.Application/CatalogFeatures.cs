using SharedKernel.FeatureManagement;

namespace Shop.Catalog.Application;

/// <summary>The catalog's feature flags, configured under <c>feature_management</c>.</summary>
public static class CatalogFeatures
{
    /// <summary>Marks products created in the last week with a "new" badge; targeted per tenant.</summary>
    public static readonly FeatureFlag<bool> NewArrivalBadge = FeatureFlag.Boolean(
        "NewArrivalBadge",
        description: "Badge recently added products as new."
    );
}
