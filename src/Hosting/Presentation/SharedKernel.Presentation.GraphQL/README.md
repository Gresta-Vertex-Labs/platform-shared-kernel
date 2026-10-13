# SharedKernel.Presentation.GraphQL

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Host](https://img.shields.io/badge/tier-Host-d73a49)

> **HotChocolate v16 server conventions in one call: snake_case filter operations, capped paging, errors that carry
> the members of an RFC 9457 problem (status, error code, trace id, correlation id) like a REST error does, an
> introspection switch, and a paged response type that matches `SharedKernel.Contracts`' `PagedList<T>`.** Pick it when a service exposes a GraphQL schema next to (or instead of)
> its REST API; REST endpoints use `SharedKernel.Presentation.WebApi`.

| You get | So that |
| --- | --- |
| `services.AddSharedKernelGraphQL(o => …)` | Filtering, sorting, paging and the error filter are set before your own types, the same in every service |
| `FilterBase<T>` / `SortBase<T>` | Filter and sort inputs follow the platform convention (`eq`, `neq`, `not_contains`, `gte`, …) |
| `MaxPageSize` (validated 1–500) | No client can ask for an unbounded page |
| `PagedResponseType<T>` | GraphQL pages look like REST's `PagedList<T>` (`totalCount` + `items`) |
| `SharedKernelErrorFilter` | Every GraphQL error carries `status`, `type`, `title`, `detail`, `errorCode`, `traceId` and `correlationId` extensions, from the same decisions as a REST problem, and no exception text leaves Development |
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
| Tier | Host — reference it from your **Api** / **Worker** project |
| Depends on | `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Contracts`, `SharedKernel.Presentation.Core`, `SharedKernel.Presentation.WebApi`, `HotChocolate.AspNetCore`, `HotChocolate.Data` |
| Namespaces | `SharedKernel.Presentation.GraphQL` (`GraphQLErrorCodes`), `.Extensions`, `.Options`, `.Types`, `.Pagination` |

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
- **Errors.** `SharedKernelErrorFilter` is the GraphQL form of the REST exception handler. A resolver's exception is
  classified as the handler does: a `ValidationException` (one error is itself, several are `validation.failed`), any
  other `SharedKernelException` by its error, a `TimeoutException` or a cancellation the client did not cause as
  `timeout.default`, anything else as the generic `unexpected.default`. An error HotChocolate raised itself for a
  request it rejected (syntax, unknown field, bad argument) has no exception and is a client error: 400
  `graphql.request_invalid` (`GraphQLErrorCodes.RequestInvalid`), its own `code` kept. The filter also runs over the
  errors of a request HotChocolate rejects before executing it, which it does not filter itself.

  | Extension | Value | Outside Development |
  | --- | --- | --- |
  | `status` | `ErrorType` status from the table REST uses (400, 401, 403, 404, 409, 422, 500, 503, 504) | the same |
  | `type`, `title` | The framework's RFC 9110 type URI and reason phrase for that status (never the code or the message) | the same |
  | `detail`, and the error's `message` | The client message: localized, and for a server error a generic sentence | the same |
  | `errorCode` | `Error.Code`, or `graphql.request_invalid` | the same |
  | `traceId`, `correlationId` | The current activity's W3C id (else the request identifier), and the request's correlation id (absent outside a request) | the same |
  | `errors`, `errorCodes` | Field errors keyed by field path (else by code), as REST's problem | the same |
  | `exception` | `type`, `message`, `stackTrace` of an unclassified exception | **never**: Development only |

  Other extensions an error already had are kept (a `status` it carried is not trusted). Without a request (an
  in-process executor) the environment is production, nothing is translated and there is no correlation id. Unlike
  REST, there is no `IncludeExceptionDetails` setting: exception text never leaves Development.
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
| `GraphQLErrorCodes.RequestInvalid` | `"graphql.request_invalid"`, the `errorCode` of an error HotChocolate raised for the request |

The filter convention and the error filter are internal; `AddSharedKernelGraphQL` registers them (and, when the host
has not, `IHttpContextAccessor` and logging).

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 14400 | Error | A server-category GraphQL error, with the exception (`Unexpected`, `Unavailable`, `Timeout`) |
| 14401 | Debug | A client-category GraphQL error, including a request HotChocolate rejected and a client that went away |

One record per error (a request can carry several), with the error code and status only: no message, variable or query.

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
| Throw a plain exception to say "not found" | Throw a `SharedKernelException` (`error.ToException()`, `GetValueOrThrow()`) | Only a classified exception gets its status and code; anything else is a 500 `unexpected.default` with a generic message |
| Read `extensions.title` or `detail` as the error code | Branch on `extensions.errorCode` | `title` is the status reason phrase and `detail` the (localized) message |
| Unpack `.Items`/`.TotalCount` in a resolver | Use `PagedResponseType<T>.FromPagedList` | Keeps the shape identical to REST pages |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Presentation packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Hosting/Presentation/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
