using SharedKernel.Domain.StronglyTypedIds;

namespace OrderApi.Domain;

/// <summary>Strongly-typed identifier for <see cref="Order"/>.</summary>
public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static OrderId New() => new(Guid.CreateVersion7());
}
