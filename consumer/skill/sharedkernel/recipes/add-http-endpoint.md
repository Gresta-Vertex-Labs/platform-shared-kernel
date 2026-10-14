---
proven-by:
  - samples/README.md
  - samples/Shop/Ordering/Shop.Ordering.Api/OrderEndpoints.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/OrdersHub.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/Program.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/Shop.Ordering.Api.csproj
  - samples/Shop/Catalog/Shop.Catalog.Api/CatalogEndpoints.cs
  - samples/Shop/Catalog/Shop.Catalog.Api/CatalogGraph.cs
  - samples/Shop/Catalog/Shop.Catalog.Api/Program.cs
  - samples/Shop/Catalog/Shop.Catalog.Api/Shop.Catalog.Api.csproj
  - samples/Shop/Inventory/Shop.Inventory.Api/InventoryGrpcService.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Program.cs
  - samples/Shop/Billing/Shop.Billing.Api/BillingEndpoints.cs
unproven:
  - ToOkWithETag and If-Match concurrency: no Shop endpoint returns an ETag through the typed result
---
# Add an HTTP, gRPC, SignalR or GraphQL endpoint

**Use when:** exposing an existing command or query over HTTP (minimal APIs), gRPC, a SignalR hub or GraphQL.
**Not when:** calling another service (add-outbound-call.md), deciding who the caller is (add-authentication.md),
or making a POST safe to retry (add-idempotency.md).

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Api | `SharedKernel.Presentation.WebApi` | problem details, typed results, `IEndpointModule` and the generated `MapEndpoints()`; brings `SharedKernel.Presentation.Core` |
| Api | `SharedKernel.Presentation.OpenApi` | API versioning, one OpenAPI document per version |
| Api | `SharedKernel.Presentation.Grpc` | gRPC services with the same error contract and authorization |
| Api | `SharedKernel.Presentation.SignalR` | hubs on the same error contract, in the caller's request context |
| Api | `SharedKernel.Presentation.GraphQL` | HotChocolate conventions, errors that carry the error code |

The Application project does not change: an endpoint only translates a request into a command or query.

## Steps

1. **Domain / Application.** Nothing new. The command or query, its handler and its `[RequirePermission]` already
   exist (new-service.md). Permissions live on the request, never on the endpoint alone.

2. **Infrastructure.** Nothing new.

3. **Api: an HTTP endpoint module.** One class per area. Send through `ISender` and map the `Result` with one
   typed-result call (`ToOk`, `ToCreated`, `ToAccepted`, `ToNoContent`, `ToOkWithETag`, `ToHttpResult`); never
   branch on `IsSuccess` or pick a failure status yourself.

   ```csharp
   using SharedKernel.Application.Messaging;
   using SharedKernel.Presentation.WebApi;
   using Shop.Ordering.Application;

   public sealed record OrderPlacedResponse(Guid Id);

   public sealed class OrderEndpoints : IEndpointModule
   {
       public static void Map(IEndpointRouteBuilder app)
       {
           var orders = app.MapGroup("/orders").RequireAuthorization();

           orders
               .MapPost("/", (PlaceOrderRequest body, IdempotencyKey key, ISender sender, CancellationToken ct) =>
                   sender
                       .Send(new PlaceOrderCommand(body.CustomerEmail, body.ShippingAddress, body.Currency,
                           body.Lines, body.PaymentToken, key.Value), ct)
                       .ToCreated(id => $"/orders/{id}", id => new OrderPlacedResponse(id)))
               .WithName("PlaceOrder");

           orders
               .MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
                   sender.Send(new GetOrderQuery(id), ct).ToOk())
               .WithName("GetOrder");
       }
   }
   ```

   Host wiring, in `Program.cs`:

   ```csharp
   using SharedKernel.Presentation.WebApi;
   using SharedKernel.ServiceDefaults.Security;

   builder.AddSharedKernelWebApi();
   // …
   app.UseSharedKernelRequestContext();
   app.UseSharedKernelWebApi();
   app.MapEndpoints();
   ```

   `IdempotencyKey` binds the `Idempotency-Key` header; see add-idempotency.md for the rest of that flow.

4. **Api: OpenAPI and versioning.** Version a group with `NewVersionedApi(…)` and `HasApiVersion(…)`:

   ```csharp
   using SharedKernel.Application.Messaging;
   using SharedKernel.Presentation.OpenApi;
   using SharedKernel.Presentation.WebApi;

   builder.AddSharedKernelOpenApi(options => options.Title = "Shop Catalog API");
   // …
   app.MapSharedKernelOpenApi();   // Development only unless the options expose it

   // in an IEndpointModule
   var products = app.NewVersionedApi("Products")
       .MapGroup("/products")
       .HasApiVersion(1.0)
       .RequireAuthorization();
   products
       .MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
           sender.Send(new GetProductQuery(id), ct).ToOk())
       .WithName("GetProduct");
   ```

5. **Api: a gRPC service.** Unwrap the `Result` with `GetValueOrThrow()`; the kernel's interceptor turns the failure
   into the matching status with rich error details. Never build an `RpcException` yourself.

   ```csharp
   using Grpc.Core;
   using SharedKernel.Application.Messaging;
   using SharedKernel.Core.Extensions;

   public sealed class InventoryGrpcService(ISender sender) : InventoryService.InventoryServiceBase
   {
       public override async Task<ReleaseStockReply> ReleaseStock(
           ReleaseStockRequest request, ServerCallContext context)
       {
           int released = await sender
               .Send(new ReleaseStockCommand(Guid.Parse(request.OrderId)), context.CancellationToken)
               .GetValueOrThrow();
           return new ReleaseStockReply { ReleasedLines = released };
       }
   }
   ```

   ```csharp
   using SharedKernel.Presentation.Grpc;

   builder.AddSharedKernelGrpc(options => options.ErrorDomain = "inventory.shop.example");
   // …
   app.MapGrpcService<InventoryGrpcService>().RequireAuthorization("ShopService");
   ```

   A gRPC host does not need the WebApi package; the reference platform runs both side by side.

6. **Api: a SignalR hub.** Guard the hub with the shared attributes; it runs in the caller's request context:

   ```csharp
   using Microsoft.AspNetCore.SignalR;
   using SharedKernel.Execution.Context;
   using SharedKernel.Presentation.Authorization;

   [RequireEndpointPermission(OrderingPermissions.Read)]
   public sealed class OrdersHub(IRequestContext caller) : Hub
   {
       public const string Path = "/hubs/orders";

       public override async Task OnConnectedAsync()
       {
           if (caller.TenantId is { } tenant)
           {
               await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenant.Value));
           }
           await base.OnConnectedAsync();
       }

       internal static string TenantGroup(Guid tenant) => $"tenant:{tenant:D}";
   }
   ```

   ```csharp
   using SharedKernel.Presentation.SignalR;

   builder.AddSharedKernelSignalR();
   // …
   app.MapHub<OrdersHub>(OrdersHub.Path);
   ```

   To push from a handler, put a port in Application (`IOrderStatusNotifier`) and implement it in the Api over
   `IHubContext<OrdersHub>`.

7. **Api: GraphQL.** A resolver sends the same use case as the REST endpoint:

   ```csharp
   using HotChocolate;
   using SharedKernel.Application.Messaging;

   public sealed class CatalogQueries
   {
       public async Task<ProductDto> GetProductAsync(
           Guid id, [Service] ISender sender, CancellationToken cancellationToken) =>
           GraphResult.Unwrap(await sender.Send(new GetProductQuery(id), cancellationToken));
   }
   ```

   ```csharp
   using SharedKernel.Presentation.GraphQL.Extensions;

   builder.Services
       .AddSharedKernelGraphQL(options => options.AllowIntrospection = builder.Environment.IsDevelopment())
       .AddQueryType<CatalogQueries>();
   // …
   app.MapGraphQL().RequireAuthorization();
   ```

   `GraphResult.Unwrap` is the service's own helper: it throws a `GraphQLException` carrying `Error.Code` and the
   HTTP status the REST boundary would use. The `SharedKernel.Presentation.GraphQL` README describes the shape the
   error filter reads.

8. **Configuration.** WebApi binds `SharedKernel:Presentation:WebApi`; the other packages take options in code. The
   keys are in each README.

## Test it

Unit-test the use case through the pipeline, not the endpoint (write-tests.md). For a gRPC service, call it directly
with `TestServerCallContext.Create()` from `SharedKernel.Presentation.Testing` and an `ISender` stub. Host-level
tests use `WebApplicationFactory<Program>`; keep `public partial class Program;` at the end of `Program.cs`.

## Check

- `SK0036` flags a hand-built `RpcException` or `Status`; return a `Result` and let the interceptor map it.
- `SK0031` flags injecting `IHttpContextAccessor`, `HttpContext` or `ClaimsPrincipal` into a constructor: read
  `IRequestContext` instead.
- The `MapEndpoints()` generator reports a module it cannot map: abstract (`SKEP001`), generic (`SKEP002`),
  unreachable from the assembly (`SKEP003`), or inheriting `Map` from another module (`SKEP004`).
- Every error is an RFC 9457 `application/problem+json` body with `errorCode`, `traceId` and `correlationId`; an
  anonymous caller gets 401, a caller without the permission 403.

## Read next

`SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.Core`, `SharedKernel.Presentation.OpenApi`,
`SharedKernel.Presentation.Grpc`, `SharedKernel.Presentation.SignalR`, `SharedKernel.Presentation.GraphQL`.
