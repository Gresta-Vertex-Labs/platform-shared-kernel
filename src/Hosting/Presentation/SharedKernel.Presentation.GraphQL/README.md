# SharedKernel.Presentation.GraphQL

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)

> **HotChocolate v16 server conventions in one call: snake_case filter operations, capped paging, a
> ProblemDetails-shaped error extension, an introspection switch, and a paged response type that matches
> `SharedKernel.Contracts`' `PagedList<T>`.**

| You get | So that |
| --- | --- |
| `services.AddSharedKernelGraphQL(o => …)` | Filtering, sorting, paging and the error filter are set before your own types, the same in every service |
| `FilterBase<T>` / `SortBase<T>` | Filter and sort inputs follow the platform convention (`eq`, `neq`, `not_contains`, `gte`, …) |
| `MaxPageSize` (validated 1–500) | No client can ask for an unbounded page |
| `PagedResponseType<T>` | GraphQL pages look like REST's `PagedList<T>` (`totalCount` + `items`) |
| `SharedKernelErrorFilter` | Every GraphQL error carries `status`, `title` and `type` extensions, like a REST problem |
| `AllowIntrospection` | The schema can be hidden in production with one switch |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Presentation.GraphQL" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Host — reference it from your **Api** project |
| Depends on | `SharedKernel.Primitives`, `SharedKernel.Contracts`, `HotChocolate.AspNetCore`, `HotChocolate.Data` |
| Namespaces | `SharedKernel.Presentation.GraphQL.Extensions`, `.Options`, `.Types`, `.Pagination` |

HotChocolate v16 is not AOT-safe; do not add `<IsAotCompatible>true</IsAotCompatible>` to a project that references
this package.

## Quick start

```csharp
using SharedKernel.Presentation.GraphQL.Extensions;

builder.Services.AddSharedKernelGraphQL(options =>
    {
        options.AllowIntrospection = builder.Environment.IsDevelopment();
        options.MaxPageSize = 50;
    })
    .AddQueryType<OrderQueries>()
    .AddType<OrderFilterType>()
    .AddType<OrderSortType>();

var app = builder.Build();
app.MapGraphQL();
```

`AddSharedKernelGraphQL` registers HotChocolate's server (`AddGraphQLServer()`, with its default security policy: cost
analysis and request limits) and returns its `IRequestExecutorBuilder`; call it **before** any service-specific
`AddGraphQLServer()`/`AddTypes()` so every type inherits the conventions. `AllowIntrospection` decides introspection in
every environment; HotChocolate's own policy would otherwise disable it outside Development.

## How it works

- **Filtering** (`EnableFiltering`) registers `HotChocolate.Data` filtering with the platform convention, which renames
  the operations: `eq`, `neq`, `contains`, `not_contains`, `starts_with`, `not_starts_with`, `ends_with`,
  `not_ends_with`, `in`, `not_in`, `gt`, `gte`, `lt`, `lte`.
- **Sorting** (`EnableSorting`) registers HotChocolate's default sorting.
- **Paging** (`EnablePaging`) registers the queryable cursor-paging provider with `MaxPageSize`, a default page size of
  `min(10, MaxPageSize)`, and `IncludeTotalCount`.
- **Errors.** `SharedKernelErrorFilter` keeps each error's extensions and sets `status` (from a `status` extension,
  else 500), `title` (the error message), `detail` (the exception's message, when there is one) and, unless already
  set, a `type` URI for 400/401/403/404/409/422 or a generic server-error URI.
- **Introspection** is disabled when `AllowIntrospection` is `false`.
- **Validation.** `MaxPageSize` outside `1..500` throws `OptionsValidationException` at the call, before any schema is
  built. A second call returns the existing builder and changes nothing.

## Recipes

### 1. Filter and sort types

Extend the platform bases rather than HotChocolate's `FilterInputType<T>`/`SortInputType<T>`:

```csharp
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using SharedKernel.Presentation.GraphQL.Types;

public sealed class OrderFilterType : FilterBase<Order>
{
    protected override void Configure(IFilterInputTypeDescriptor<Order> descriptor)
    {
        descriptor.Field(o => o.Status);
        descriptor.Field(o => o.TotalAmount);
    }
}

public sealed class OrderSortType : SortBase<Order>
{
    protected override void Configure(ISortInputTypeDescriptor<Order> descriptor) =>
        descriptor.Field(o => o.CreatedAt);
}
```

### 2. Return a page from the application layer

```csharp
using SharedKernel.Contracts.Pagination;
using SharedKernel.Presentation.GraphQL.Pagination;

public sealed class OrderQueries
{
    public async Task<PagedResponseType<OrderDto>> GetOrdersAsync(
        int page, int pageSize, [Service] IOrderReadService orders, CancellationToken ct)
    {
        PagedList<OrderDto> pagedList = await orders.GetPagedAsync(page, pageSize, ct);
        return PagedResponseType<OrderDto>.FromPagedList(pagedList);
    }
}
```

Use `FromPagedList` for a `PagedList<T>`, `FromPage(IPage)` or `FromConnection(Connection<T>)` for a HotChocolate-paged
source, and `From(items, totalCount)` to assemble one by hand. `TotalCount` is a `long` (the `Long` scalar), matching
`PagedList<T>.TotalCount`.

## Configuration

Set in code on `AddSharedKernelGraphQL(o => …)` (`GraphQLOptions`); nothing is bound from configuration.

| Option | Type | Default | Meaning |
| --- | --- | --- | --- |
| `EnableFiltering` | `bool` | `true` | `HotChocolate.Data` filtering with the platform convention |
| `EnableSorting` | `bool` | `true` | `HotChocolate.Data` sorting |
| `EnablePaging` | `bool` | `true` | Cursor paging capped at `MaxPageSize` |
| `MaxPageSize` | `int` | `100` | Largest page a paging argument may request; `1..500` |
| `AllowIntrospection` | `bool` | `true` | Schema introspection; set `false` in production |

## Reference

| Member | Purpose |
| --- | --- |
| `IServiceCollection.AddSharedKernelGraphQL(Action<GraphQLOptions>?)` | Registers the conventions; returns `IRequestExecutorBuilder` |
| `FilterBase<T>` | Base for filter input types (`FilterInputType<T>`) |
| `SortBase<T>` | Base for sort input types (`SortInputType<T>`) |
| `PagedResponseType<T>` | `TotalCount` (`long`), `Items`; `FromPagedList`, `FromPage`, `FromConnection`, `From` |
| `GraphQLOptions` | The options above |

The filter convention and the error filter are internal; `AddSharedKernelGraphQL` registers them. The package logs
nothing of its own.

## Testing

Build the real schema in a unit test and execute a request against it:

```csharp
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.GraphQL.Extensions;

var services = new ServiceCollection();
services.AddSharedKernelGraphQL().AddQueryType<OrderQueries>();
IRequestExecutor executor = await services.BuildServiceProvider().GetRequestExecutorAsync();

IExecutionResult result = await executor.ExecuteAsync("{ orders(page: 1, pageSize: 5) { totalCount } }");
```

[`SharedKernel.Presentation.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/SharedKernel.Presentation.Testing/README.md)'s
`GraphQLTestExecutorFactory.Create(services)` gives a plain HotChocolate server with test-safe paging for schema tests
that do not need the platform conventions.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Register `FilterInputType<T>`/`SortInputType<T>` directly | Extend `FilterBase<T>`/`SortBase<T>` | The platform convention and naming apply through the bases |
| Call `AddGraphQL()` before `AddSharedKernelGraphQL()` | Call `AddSharedKernelGraphQL()` first | Types registered earlier miss the conventions |
| Leave `AllowIntrospection` on in production | Set it from the environment | Introspection publishes the whole schema |
| Put secrets or internals in exception messages | Throw with client-safe messages, or return `Result` errors | The error filter copies the exception's message into `detail` |
| Unpack `.Items`/`.TotalCount` in a resolver | Use `PagedResponseType<T>.FromPagedList` | Keeps the shape identical to REST pages |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Presentation domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
