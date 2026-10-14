---
proven-by:
  - samples/README.md
  - samples/Shop/Directory.Build.props
  - samples/Shop/Ordering/Shop.Ordering.Domain/Shop.Ordering.Domain.csproj
  - samples/Shop/Ordering/Shop.Ordering.Domain/Order.cs
  - samples/Shop/Ordering/Shop.Ordering.Application/Shop.Ordering.Application.csproj
  - samples/Shop/Ordering/Shop.Ordering.Application/OrderCommands.cs
  - samples/Shop/Ordering/Shop.Ordering.Application/OrderingPorts.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/Shop.Ordering.Infrastructure.csproj
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/OrderingInfrastructure.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/Shop.Ordering.Api.csproj
  - samples/Shop/Ordering/Shop.Ordering.Api/Program.cs
  - samples/Shop/Ordering/Shop.Ordering.Tests/OrderingTests.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/CatalogFeatures.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/Products/GetProduct.cs
  - samples/Shop/Catalog/Shop.Catalog.Api/Program.cs
  - samples/Shop/Billing/Shop.Billing.Api/Program.cs
  - samples/Shop/Billing/Shop.Billing.Api/Security/BillingKeyVault.cs
unproven:
  - A readiness probe of the service's own: every Shop dependency is a kernel adapter that registers its own probe
  - Rate limiting: no Shop service limits requests
---
# Start a new service

**Use when:** creating a service from nothing: the four projects, what each references, `Program.cs`, the
request pipeline, health, readiness and telemetry. **Not when:** the service exists and you are adding one
capability: open that capability's recipe (add-persistence.md, add-http-endpoint.md, add-messaging.md, …).

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Domain | `SharedKernel.Domain` | aggregates, value objects, ids, rules, `Money`; brings `Result`, `Error`, `IClock`, `TenantId` |
| Application | `SharedKernel.Application` | `ICommand`/`IQuery`, handlers, `ISender`, `[RequirePermission]`, pipeline markers |
| Application | the `.Abstractions` packages its ports need | e.g. `SharedKernel.Persistence.Abstractions` for `IRepository<T, TId>` |
| Infrastructure | adapters, plus `SharedKernel.Validation.FluentValidation` | the implementations of the Application's ports |
| Api/Worker | `SharedKernel.ServiceDefaults`, `SharedKernel.ServiceDefaults.Security` | telemetry, health, startup gate, request context |
| Api/Worker | `SharedKernel.Application.Pipeline`, `SharedKernel.Application.Mediator.MediatR` | handlers and behaviors in one call, MediatR behind `ISender` |
| Api/Worker | `SharedKernel.Presentation.WebApi` and an authentication package | the HTTP boundary (add-http-endpoint.md, add-authentication.md) |
| every project | `SharedKernel.Analyzers`, `SharedKernel.Linter` | the platform rules as compiler diagnostics, the format check |
| Tests | `SharedKernel.Testing`, `SharedKernel.Application.Testing`, `SharedKernel.ArchitectureTests` | write-tests.md |

References: Domain ← Application ← Infrastructure; the Api references Application and Infrastructure; nothing references the Api.

## Steps

1. **Configuration (do this first): one version, analyzers everywhere.** Every `SharedKernel.*` package points at
   one property; never pin one package apart or float the version.

   ```xml
   <!-- Directory.Packages.props -->
   <Project>
     <PropertyGroup>
       <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
       <SharedKernelVersion>{{SharedKernelVersion}}</SharedKernelVersion>
     </PropertyGroup>
     <ItemGroup>
       <PackageVersion Include="SharedKernel.Analyzers" Version="$(SharedKernelVersion)" />
       <PackageVersion Include="SharedKernel.Linter" Version="$(SharedKernelVersion)" />
       <PackageVersion Include="SharedKernel.Domain" Version="$(SharedKernelVersion)" />
       <!-- one line per SharedKernel package the service references, always $(SharedKernelVersion) -->
     </ItemGroup>
   </Project>
   ```

   ```xml
   <!-- Directory.Build.props -->
   <Project>
     <PropertyGroup>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="SharedKernel.Analyzers" PrivateAssets="all" />
       <PackageReference Include="SharedKernel.Linter" PrivateAssets="all" />
     </ItemGroup>
   </Project>
   ```

   Or raise the rules you rely on to `error` in `.editorconfig`. Map `SharedKernel.*` to the kernel's feed (`NuGet.Config`).

2. **Domain.** References `SharedKernel.Domain` only. An aggregate takes `IClock`, checks business rules and returns
   `Result` for expected failures:

   ```csharp
   using SharedKernel.Domain.Aggregates;
   using SharedKernel.Domain.StronglyTypedIds;
   using SharedKernel.Execution.Tenancy;
   using SharedKernel.Primitives.Clocks;
   using SharedKernel.Primitives.Results;

   public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value)
   {
       public static OrderId New() => new(Guid.CreateVersion7());
   }

   public sealed class Order : TenantedAuditableAggregateRoot<OrderId>
   {
       private Order(OrderId id, TenantId tenantId, string customerEmail, string shippingAddress, IClock clock)
           : base(id, tenantId, clock) { /* … */ }

       public Result Confirm(Guid reservationId)
       {
           if (Status != OrderStatus.Placed)
           {
               return Result.Failure(OrderErrors.InvalidTransition(Id, Status, OrderStatus.Confirmed));
           }
           Status = OrderStatus.Confirmed;
           RaiseDomainEvent(at => new OrderStatusChanged(Id, TenantId, Status) { OccurredOn = at });
           return Result.Success();
       }
   }
   ```

3. **Application.** References its Domain project, `SharedKernel.Application` and the `.Abstractions` it needs. A
   use case is a record plus a handler; the permission is declared on the request; the tenant fails closed:

   ```csharp
   using SharedKernel.Application.Authorization;
   using SharedKernel.Application.Messaging;
   using SharedKernel.Persistence.Abstractions.Repositories;
   using SharedKernel.Primitives.Errors;
   using SharedKernel.Primitives.Results;

   public static class OrderingPermissions
   {
       public const string Read = "orders.read";
   }

   [RequirePermission(OrderingPermissions.Read)]
   public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

   public sealed class GetOrderHandler(IReadRepository<Order, OrderId> orders)
       : IQueryHandler<GetOrderQuery, OrderDto>
   {
       public async Task<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct)
       {
           var order = await orders.GetByIdAsync(new OrderId(query.OrderId), ct);
           return order is null
               ? Result<OrderDto>.Failure(
                   Error.NotFound("ordering.order.not_found", $"Order {query.OrderId} was not found."))
               : Result<OrderDto>.Success(/* map the order */);
       }
   }
   ```

   Commands: `ICommand`/`ICommand<T>` + `ICommandHandler<,>`. Validators: `AbstractValidator<T>` or
   `IRequestValidator<T>`. Domain events: `IDomainEventHandler<TEvent>`. The caller: inject `IRequestContext`.

4. **Infrastructure.** References its Application project and adapter packages only. One extension method
   registers every adapter; each adapter registers its own readiness probe:

   ```csharp
   using Microsoft.Extensions.DependencyInjection;
   using Microsoft.Extensions.Hosting;
   using SharedKernel.Primitives.Clocks;
   using SharedKernel.Validation.FluentValidation;

   public static class OrderingInfrastructure
   {
       public static IHostApplicationBuilder AddOrderingInfrastructure(this IHostApplicationBuilder builder)
       {
           builder.Services.AddClock();
           // adapters: add-persistence.md, add-messaging.md, add-caching.md, add-outbound-call.md, …
           builder.Services.AddFluentValidationRequestValidators(typeof(PlaceOrderCommand).Assembly);
           return builder;
       }
   }
   ```

5. **Api/Worker.** References its Application and Infrastructure projects and Host packages only. Register in this
   order; the middleware order is fixed:

   ```csharp
   using SharedKernel.Application.Mediator.MediatR;
   using SharedKernel.Application.Pipeline;
   using SharedKernel.Presentation.WebApi;
   using SharedKernel.Security.Oidc.Extensions;
   using SharedKernel.ServiceDefaults.Extensions;
   using SharedKernel.ServiceDefaults.HealthChecks;
   using SharedKernel.ServiceDefaults.Probes;
   using SharedKernel.ServiceDefaults.Security;
   using SharedKernel.ServiceDefaults.Telemetry;

   var builder = WebApplication.CreateBuilder(args);

   builder.AddServiceDefaults();                 // OpenTelemetry, health endpoints, the startup gate
   builder.WithPersistenceTelemetry();           // one With…Telemetry() per third-party library you use

   builder.Services.AddOidcAuthentication(builder.Configuration);
   builder.Services.AddAuthorization();
   builder.Services.AddSharedKernelRequestContext();

   builder.AddOrderingInfrastructure();

   builder.Services.AddSharedKernelApplication(
       typeof(PlaceOrderCommand).Assembly,
       app => app.UseMediatR().WithTransactions());   // add WithIdempotency(), WithAuditing(), … as needed

   builder.Services.AddHealthChecks().AddSharedKernelReadiness();
   builder.AddSharedKernelWebApi();

   var app = builder.Build();

   app.UseSharedKernelRequestContext();          // first: correlation id and caller scope wrap everything
   app.UseSharedKernelWebApi();
   app.MapDefaultHealthCheckEndpoints();         // liveness and readiness
   app.MapEndpoints();                           // every IEndpointModule, generated at compile time

   app.Lifetime.ApplicationStarted.Register(() =>
       app.Services.GetRequiredService<StartupGate>().MarkReady());

   await app.RunAsync();

   public partial class Program;
   ```

   A messaging bus is registered after `AddSharedKernelRequestContext()` (add-messaging.md). Each `With…()` opt-in
   needs its seam (`IUnitOfWork`, `IIdempotencyStore`, `IAuditTrailWriter`), checked when the host starts.

6. **Smaller host needs.**
   - Feature flags: declare `FeatureFlag.Boolean("Key", …)` constants in Application, read them through OpenFeature's
     `IFeatureClient`, register with `AddSharedKernelFeatureManagement(configuration, …)` in the Api
     (`SharedKernel.FeatureManagement`).
   - Secrets from Azure Key Vault as configuration: call `AddSharedKernelKeyVaultConfiguration(…)` first, before
     anything that reads them (`SharedKernel.ServiceDefaults.Configuration.KeyVault`).
   - Request culture: `AddSharedKernelLocalization()` and `UseRequestLocalization()` inside the WebApi pipeline's
     `BeforeAuthorization` hook (`SharedKernel.ServiceDefaults.Localization`, messages in `SharedKernel.Localization`).
   - Not exercised by the reference platform, so read the README first: your own dependency's readiness
     (`IReadinessProbe` + `AddReadinessProbe<T>()`, `SharedKernel.Primitives`) and `AddSharedKernelRateLimiting(…)`.
   - IBAN, VAT and similar values: `SharedKernel.Validation`; personal-data redaction: `SharedKernel.DataPrivacy`.

## Test it

Add a Tests project that references the Api project (write-tests.md). Run the use cases through
`ApplicationPipelineTestHarness`, and assert the project shape: Domain sees Foundation and Model packages only,
Application no adapter, host package or MediatR, Infrastructure no host package, production no Testing package.

## Check

- A wrong placement (a Host package in Infrastructure, an adapter in Application) is caught by the architecture
  tests in the Tests project: write them on day one.
- `SK0001` flags `DateTime.UtcNow` (inject `IClock`); `SK0020`/`SK0021` flag `ILogger.LogXxx` and
  `LoggerMessage.Define`; `SK0030` flags a `Result` nobody reads; `SK0003` a raw `throw new Exception`.
- `/health/ready` stays unhealthy until `StartupGate.MarkReady()` runs and every probe passes.

## Read next

`SharedKernel.ServiceDefaults`, `SharedKernel.Application.Pipeline`, `SharedKernel.Application`,
`SharedKernel.Domain`, `SharedKernel.Presentation.WebApi`, `SharedKernel.Analyzers`.
