# SharedKernel.Presentation.GraphQL

HotChocolate v16 server-side GraphQL conventions for Platform.SharedKernel microservices:
`SharedKernelFilterConvention` (snake_case filter operations), the `FilterBase<T>`/`SortBase<T>`
abstract base types, `PagedResponseType<T>` (`TotalCount` + `Items`, aligned with
`SharedKernel.Contracts`' `PagedList<T>`), `SharedKernelErrorFilter` (`IError` →
`ProblemDetails`-compatible shape), and the `AddSharedKernelGraphQL` DI entry point.

**Tier:** Host. A GraphQL server is an inbound API surface, so the package lives beside WebApi, gRPC
and SignalR in `14.Presentation`. It replaces `SharedKernel.Communication.GraphQL` from
`11.Communication`; the types are unchanged, only the package id and the root namespace moved
(`SharedKernel.Communication.GraphQL.*` → `SharedKernel.Presentation.GraphQL.*`).

**HotChocolate v16 is not AOT-safe.** Do not add `<IsAotCompatible>true</IsAotCompatible>` to any
project that references this package.

## Install

```xml
<PackageReference Include="SharedKernel.Presentation.GraphQL" />
```

Versions come from your single `SharedKernelVersion`; every SharedKernel package ships with the
same version.

| Namespace | Types |
| --- | --- |
| `SharedKernel.Presentation.GraphQL.Extensions` | `AddSharedKernelGraphQL` |
| `SharedKernel.Presentation.GraphQL.Options` | `GraphQLOptions` |
| `SharedKernel.Presentation.GraphQL.Types` | `FilterBase<T>`, `SortBase<T>` |
| `SharedKernel.Presentation.GraphQL.Pagination` | `PagedResponseType<T>` |

`SharedKernelFilterConvention` and `SharedKernelErrorFilter` are internal; `AddSharedKernelGraphQL` registers them.

## Usage

`AddSharedKernelGraphQL` must be called **before** any service-specific `AddGraphQL()`/`AddTypes()`
calls — it establishes the base convention every type inherits:

```csharp
services.AddSharedKernelGraphQL(options =>
{
    options.AllowIntrospection = builder.Environment.IsDevelopment();
    options.MaxPageSize = 50;
})
.AddQueryType<QueryType>()
.AddType<OrderFilterType>();   // OrderFilterType : FilterBase<Order>
                                // OrderSortType   : SortBase<Order>
```

`AddSharedKernelGraphQL` throws `OptionsValidationException` synchronously — at the call site, before
any HotChocolate schema is built — when `GraphQLOptions.MaxPageSize` is outside `1..500`.

## Recipe: `FilterBase<T>`/`SortBase<T>` instead of the raw HotChocolate base types

Direct registration of `FilterInputType<T>`/`SortInputType<T>` is a platform violation — always
extend the wrapper types, which enforce the platform's snake_case field-binding convention:

```csharp
public sealed class OrderFilterType : FilterBase<Order>
{
    protected override void Descriptor(IFilterInputTypeDescriptor<Order> descriptor)
    {
        descriptor.Field(o => o.Status);
        descriptor.Field(o => o.TotalAmount);
    }
}

public sealed class OrderSortType : SortBase<Order>
{
    protected override void Descriptor(ISortInputTypeDescriptor<Order> descriptor)
    {
        descriptor.Field(o => o.CreatedAt);
    }
}
```

## Recipe: `PagedResponseType<T>` from a `PagedList<T>` source

```csharp
public sealed class OrderQueries
{
    public async Task<PagedResponseType<OrderDto>> GetOrdersAsync(
        int page, int pageSize, IOrderReadService orderReadService, CancellationToken ct)
    {
        PagedList<OrderDto> pagedList = await orderReadService.GetPagedAsync(page, pageSize, ct);
        return PagedResponseType<OrderDto>.FromPagedList(pagedList);
    }
}
```

Never unpack `.Items`/`.TotalCount` manually in a resolver — use `FromPagedList` for an
application-layer `PagedList<T>` source, or `FromPage`/`FromConnection` for a HotChocolate-paged
source (`IPage`/`Connection<T>`).

`TotalCount` is a `long`, matching `PagedList<T>.TotalCount`, and appears in the schema as the `Long`
scalar rather than `Int`. HotChocolate's own `IPage`/`Connection<T>` totals are `int` and widen without
loss.

## Options

| `GraphQLOptions` member | Default | Meaning |
| --- | --- | --- |
| `EnableFiltering` | `true` | Registers `HotChocolate.Data` filtering with `SharedKernelFilterConvention`. |
| `EnableSorting` | `true` | Registers `HotChocolate.Data` sorting. |
| `EnablePaging` | `true` | Offset and cursor paging, capped at `MaxPageSize`. |
| `MaxPageSize` | `100` | Largest page a paging argument may request; must be `1..500`. |
| `AllowIntrospection` | `true` | Schema introspection. Set it to `false` in production. |

## Dependencies

```text
SharedKernel.Presentation.GraphQL  →  SharedKernel.Primitives, SharedKernel.Contracts,
                                        HotChocolate.Data, HotChocolate.AspNetCore
```

Target framework: `net10.0`. It references no mediator, persistence, caching or messaging package.

See the domain [README](../README.md) and [`CLAUDE.md`](../CLAUDE.md) for how it fits with the other
presentation packages.
