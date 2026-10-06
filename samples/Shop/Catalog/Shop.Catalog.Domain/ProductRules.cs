using System.Text.RegularExpressions;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Monetary;

namespace Shop.Catalog.Domain;

/// <summary>A product needs a name of at most <see cref="Product.MaxNameLength"/> characters.</summary>
public sealed class ProductNameIsRequired(string? name) : IBusinessRule
{
    public string Code => "catalog.product.name_required";

    public string Message =>
        $"A product needs a name of at most {Product.MaxNameLength} characters.";

    public bool IsBroken() =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > Product.MaxNameLength;
}

/// <summary>A SKU is 3 to 32 letters, digits and dashes.</summary>
public sealed partial class SkuIsWellFormed(string? sku) : IBusinessRule
{
    public string Code => "catalog.product.sku_invalid";

    public string Message => "A SKU is 3 to 32 letters, digits and dashes.";

    public bool IsBroken() => sku is null || !SkuPattern().IsMatch(sku.Trim());

    [GeneratedRegex("^[A-Za-z0-9-]{3,32}$")]
    private static partial Regex SkuPattern();
}

/// <summary>A price is greater than zero.</summary>
public sealed class PriceIsPositive(Money price) : IBusinessRule
{
    public string Code => "catalog.product.price_not_positive";

    public string Message => "A product price must be greater than zero.";

    public bool IsBroken() => !price.IsPositive;
}
