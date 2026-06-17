---
name: project-hc-v16-patterns
description: HotChocolate v16 API specifics discovered during SK.11.GraphQL implementation — version upgrade, naming changes, test patterns
metadata:
  type: project
---

HotChocolate was upgraded from v14 (planned) to v16.1.4 because v14 is incompatible with net10.0.

## Version pins
- `HotChocolate.AspNetCore` 16.1.4
- `HotChocolate.Data` 16.1.4

## DefaultFilterOperations constant names
HC v16 uses `LowerThan` (= 20) and `LowerThanOrEquals` (= 22). The names `LessThan` / `LessThanOrEquals` do **not exist**. Using wrong names causes a runtime error.

## Offset paging — no CollectionSegment concrete type
HC v16 offset paging returns `IPage` (not `CollectionSegment<T>`). To extract data:
- `IPageTotalCountProvider.TotalCount` (cast `IPage as IPageTotalCountProvider`)
- `IPage.Items.OfType<T>()` for typed items

## Cursor paging
- Use `Connection<T>` with `IEdge<T>` edges
- Factory: `IndexEdge<T>.Create(T node, int index)`
- Constructor: `new Connection<T>(edges, pageInfo, totalCount)`
- `connection.Edges.Select(e => e.Node)` for typed items

## IExecutionResult vs OperationResult
`IExecutionResult.Errors` does NOT exist in HC v16. Must cast:
```csharp
var operationResult = result.ExpectOperationResult(); // OperationResult
operationResult.Errors // IReadOnlyList<IError>?
```

## InputField.Name is string
In HC v16, `InputField.Name` is `string`, not `NameString`. Use `f.Name` directly — never `f.Name.Value`.

## Test schema types must be public
HC v16 reflection cannot discover private or private-nested classes for schema building. All types used in `AddQueryType<T>()`, `[UseFiltering(typeof(T))]`, `[UseSorting(typeof(T))]` must be `public`. Private nested classes cause:
`HotChocolate.SchemaException: Unable to infer or resolve a schema type from the type reference 'Product (Output)'`

## Custom filter convention registration
```csharp
builder.AddFiltering<SharedKernelFilterConvention>()  // HotChocolateDataRequestBuilderExtensions
       .AddSorting()
       .AddQueryableCursorPagingProvider()             // for cursor paging
```

## Introspection control
```csharp
builder.DisableIntrospection(true);  // IRequestExecutorBuilder extension
```

## Idempotency sentinel pattern
```csharp
private sealed class SharedKernelGraphQLRegistrationMarker { }
// Register as singleton; check existence before re-registering
if (services.Any(d => d.ServiceType == typeof(SharedKernelGraphQLRegistrationMarker)))
    return services.AddGraphQL(); // no-op
```

## IError.WithExtensions nullability
`IError.WithExtensions(IReadOnlyDictionary<string, object?>)` — values are nullable `object?`. Assign dict directly as `IReadOnlyDictionary<string, object?>`.

**Why:** HC v14 planned, but net10.0 compatibility required v16. Many public API surfaces changed between versions.

**How to apply:** Always verify HC API against v16.1.4 docs/source, not v14. Run a temp probe program at `C:\Temp\HC_Probe\` when uncertain about an API.
