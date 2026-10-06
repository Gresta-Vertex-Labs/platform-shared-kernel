using SharedKernel.Domain.StronglyTypedIds;

namespace Shop.Catalog.Domain;

/// <summary>Strongly-typed identifier of a <see cref="Product"/>.</summary>
public sealed record ProductId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    /// <summary>A new time-ordered id.</summary>
    public static ProductId New() => new(Guid.CreateVersion7());
}
