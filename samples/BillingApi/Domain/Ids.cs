using SharedKernel.Domain.StronglyTypedIds;

namespace BillingApi.Domain;

// Strongly-typed ids need no EF Core configuration: the conventions find every StronglyTypedId<T> reachable
// from the context and map it to its underlying column type (uuid here).

public sealed record CustomerId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static CustomerId New() => new(Guid.CreateVersion7());
}

public sealed record InvoiceId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static InvoiceId New() => new(Guid.CreateVersion7());
}

public sealed record InvoiceLineId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static InvoiceLineId New() => new(Guid.CreateVersion7());
}
