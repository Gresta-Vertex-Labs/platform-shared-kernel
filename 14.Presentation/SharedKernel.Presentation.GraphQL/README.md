# SharedKernel.Presentation.GraphQL

HotChocolate v16 server-side GraphQL conventions for Platform.SharedKernel microservices:
`SharedKernelFilterConvention` (snake_case filter operations), the `FilterBase<T>`/`SortBase<T>`
abstract base types, `PagedResponseType<T>` (`TotalCount` + `Items`, aligned with `04.Contracts`'
`PagedList<T>`), `SharedKernelErrorFilter` (`IError` → `ProblemDetails`-compatible shape), and the
`AddSharedKernelGraphQL` DI entry point.

**HotChocolate v16 is not AOT-safe.** Do not add `<IsAotCompatible>true</IsAotCompatible>` to any
project that references this package.

## Install

```bash
dotnet add package SharedKernel.Presentation.GraphQL
```

```xml
<PackageReference Include="SharedKernel.Presentation.GraphQL" Version="1.0.0" />
```

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

## Layering

```text
SharedKernel.Presentation.GraphQL  →  SharedKernel.Primitives (01.Core),
                                        SharedKernel.Contracts (04.Contracts),
                                        HotChocolate.Data, HotChocolate.AspNetCore
```

Target framework: `net10.0`. Never references `02.Caching`, `05.Application`, `06.Persistence`, or
`07.Messaging`.

For full documentation see
[`14.Presentation/CLAUDE.md`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/14.Presentation/CLAUDE.md).
