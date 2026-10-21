---
proven-by:
  - samples/README.md
  - samples/Shop/Ordering/Shop.Ordering.Tests/Shop.Ordering.Tests.csproj
  - samples/Shop/Ordering/Shop.Ordering.Tests/OrderingTests.cs
  - samples/Shop/Catalog/Shop.Catalog.Tests/Shop.Catalog.Tests.csproj
  - samples/Shop/Catalog/Shop.Catalog.Tests/CatalogPipelineTests.cs
  - samples/Shop/Catalog/Shop.Catalog.Tests/CatalogArchitectureTests.cs
  - samples/Shop/Inventory/Shop.Inventory.Tests/InventoryTests.cs
  - samples/Shop/Shop.E2E/Infrastructure/ShopPlatform.cs
  - samples/Shop/Catalog/Shop.Catalog.Tests/CatalogTenantIsolationPostgresTests.cs
---
# Write tests

**Use when:** unit-testing use cases through the real request pipeline with the kernel's fakes, testing an adapter
or host class against a double, asserting the service's project shape and the kernel's architecture rules, or
running integration flows against real containers. **Not when:** wiring the service itself (new-service.md); each
capability recipe names its own fake under "Test it".

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Tests | `SharedKernel.Testing` | `FakeClock`, `TestRequestContext`, log capture, fakers |
| Tests | `SharedKernel.Application.Testing` | `ApplicationPipelineTestHarness`: the real `AddSharedKernelApplication` pipeline |
| Tests | `SharedKernel.Persistence.Testing` | `FakeRepository<T, TId>`, the fake unit of work, the fake audit writer |
| Tests | `SharedKernel.Messaging.Testing` | `InMemoryEventPublisher`, `InMemoryMessageBus` |
| Tests | the `.Testing` package of each capability the code touches | `SharedKernel.Caching.Testing`, `SharedKernel.FeatureManagement.Testing`, `SharedKernel.Scheduling.Testing`, `SharedKernel.Presentation.Testing`, `SharedKernel.Security.Testing`, … |
| Tests | `SharedKernel.ArchitectureTests` | ready-made architecture rules for your assemblies |

Testing packages go only into Tests projects. A production project that references one is a defect.

## Steps

1. **Domain / Application / Infrastructure / Api.** Nothing changes. The Tests project references the Api project
   (that brings every layer) plus the `.Testing` packages:

   ```xml
   <ItemGroup>
     <ProjectReference Include="..\Shop.Ordering.Api\Shop.Ordering.Api.csproj" />
   </ItemGroup>
   <ItemGroup>
     <PackageReference Include="SharedKernel.Testing" />
     <PackageReference Include="SharedKernel.Application.Testing" />
     <PackageReference Include="SharedKernel.Persistence.Testing" />
     <PackageReference Include="SharedKernel.Messaging.Testing" />
     <PackageReference Include="SharedKernel.ArchitectureTests" />
     <PackageReference Include="Microsoft.NET.Test.Sdk" />
     <PackageReference Include="FluentAssertions" />
     <PackageReference Include="xunit" />
     <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
   </ItemGroup>
   ```

2. **Tests: use cases through the pipeline.** Register the fakes on the harness's `Services`, choose the same
   opt-ins as `Program.cs`, then `Build<T>()` with any type of the Application assembly. Authorization, validation,
   idempotency, transactions and auditing all run for real:

   ```csharp
   using FluentAssertions;
   using Microsoft.Extensions.DependencyInjection;
   using SharedKernel.Execution.Context;
   using SharedKernel.Execution.Tenancy;
   using SharedKernel.Persistence.Testing;
   using SharedKernel.Primitives.Clocks;
   using SharedKernel.Testing.Application;
   using SharedKernel.Testing.Clocks;
   using SharedKernel.Testing.Execution;
   using SharedKernel.Testing.Messaging;
   using Xunit;

   public sealed class OrderingPipelineTests : IDisposable
   {
       private static readonly TenantId Contoso = new(Guid.Parse("6c1d7e1a-3b52-4f8e-9a41-2f6b8c0d9e11"));

       private readonly ApplicationPipelineTestHarness _harness = new();
       private readonly FakeRepository<Order, OrderId> _orders;
       private readonly FakeAuditTrailWriter _audit;
       private readonly InMemoryEventPublisher _events = new();

       public OrderingPipelineTests()
       {
           var services = _harness.Services;
           services.AddFakeApplicationBehaviorServices();   // unit of work, idempotency store, a caller
           services.AddSingleton<IRequestContext>(
               TestRequestContext.ForTenant(Contoso).WithPermissions(OrderingPermissions.Place, OrderingPermissions.Read));
           services.AddSingleton<IClock>(new FakeClock());
           _orders = services.AddFakeRepository<Order, OrderId>();
           _audit = services.AddFakeAuditTrailWriter();
           services.AddScoped<SharedKernel.Messaging.Abstractions.EventPublisher.IEventPublisher>(_ => _events);
           _harness
               .Configure(app => app.WithIdempotency().WithTransactions().WithAuditing())
               .Build<PlaceOrderCommand>();
       }

       [Fact]
       public async Task PlaceOrder_SameKeyTwice_PlacesOnce_PublishesOnce_AndReplaysTheFirstAnswer()
       {
           var first = await _harness.SendAsync(Place("key-1"));
           var second = await _harness.SendAsync(Place("key-1"));

           second.Value.Should().Be(first.Value);
           _orders.Items.Should().ContainSingle();
           _events.PublishedOf<OrderPlaced>().Should().ContainSingle();
       }

       public void Dispose() => _harness.Dispose();

       private static PlaceOrderCommand Place(string key) =>
           new("customer@contoso.example", "1 Main Street", "EUR",
               [new PlaceOrderLine("SKU-1", 2, 12.50m)], "tok_visa", key);
   }
   ```

   Vary the caller per test: `TestRequestContext.ForTenant(t).WithPermissions(…)` for a merchant,
   `TestRequestContext.ForUser()` for a caller with no tenant (it must fail closed), a context without the
   permission (it must return `ErrorType.Forbidden` and write nothing). Advance time with `FakeClock.Advance(…)`.
   Register FluentValidation validators with `AddFluentValidationRequestValidators(assembly)` as production does.

3. **Tests: capability fakes.** Register them the way production registers the real provider:

   ```csharp
   using SharedKernel.Persistence.Testing;
   using SharedKernel.Storage;
   using SharedKernel.Testing.Caching;
   using SharedKernel.Testing.FeatureManagement;
   using SharedKernel.Testing.Storage;

   services.AddFakeUnitOfWork();
   services.AddFakeRepository<Product, ProductId>();
   services.AddFakeCachingServices();
   services.AddFakeFeatureFlags(flags => flags.SetEnabled(CatalogFeatures.NewArrivalBadge, badge));
   services.AddSharedKernelStorage().AddInMemoryTenantStore(CatalogIndexes.ImageStore);
   _harness.Configure(app => app.WithTransactions().WithCaching()).Build<CreateProductCommand>();
   ```

   Host and adapter classes that take a kernel contract are tested over a double directly, without the harness:

   ```csharp
   using SharedKernel.Testing.Caching;
   using SharedKernel.Testing.Grpc;
   using SharedKernel.Testing.Scheduling;

   var skuLocks = new SkuLocks(new FakeDistributedLockService());

   var registry = new InMemoryScheduledJobRegistry(sender);
   InventoryJobs.Register(registry, reconciliationCron: null);
   var outcome = await registry.TriggerAsync(ReconcileStockHandler.JobName, at);

   var reply = await new InventoryGrpcService(sender).ReserveStock(
       request, TestServerCallContext.Create(correlationId: "corr-1"));
   ```

4. **Tests: architecture rules.** Derive from `ArchitectureRuleBase` and assert the kernel's rules on your
   assemblies:

   ```csharp
   using System.Reflection;
   using SharedKernel.ArchitectureTests.Helpers;
   using SharedKernel.ArchitectureTests.Rules;
   using Xunit;

   public sealed class CatalogArchitectureTests : ArchitectureRuleBase
   {
       private static readonly Assembly DomainAssembly = typeof(Product).Assembly;
       private static readonly Assembly ApplicationAssembly = typeof(CreateProductCommand).Assembly;

       [Fact]
       public void Domain_NeverReadsTheSystemClock() =>
           AssertRule(DomainLayerPurityRules.DomainAssembliesNeverCallSystemClock(DomainAssembly));

       [Fact]
       public void Domain_NeverReferencesInfrastructure() =>
           AssertRule(DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure(DomainAssembly));

       [Fact]
       public void Application_NeverTouchesADatabaseTransaction() =>
           AssertRule(EfCorePackageHygieneRules.ApplicationLayerMustNotReferenceDbContextTransaction(ApplicationAssembly));
   }
   ```

   Add a project-shape test of your own over the test project's restore graph (`obj/project.assets.json`): Domain
   reaches only Foundation and Model packages, Application adds only Abstractions and no MediatR, Infrastructure no
   Host package, the Api only Host packages directly, and no production project a Testing package. The tier of
   each package is in the skill's package catalog.

5. **Tests: integration against real containers.** Keep these in a separate test project that is skipped unless an
   environment variable turns it on. The reference platform starts its whole Aspire AppHost once per run with
   `DistributedApplicationTestingBuilder.CreateAsync<Projects.Shop_AppHost>(…)`, waits for each resource to be
   healthy, and calls the services over HTTP and gRPC.

   **One database, real row-level security:** `PostgresTestServer` (`SharedKernel.Persistence.Testing`) runs
   PostgreSQL with the production roles, the application as the non-owner runtime role. Register the context exactly
   as production does (row-level security **on**), create the schema, then enable the policies:

```csharp
using SharedKernel.Execution.Transactions;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Testing;
using SharedKernel.Testing.Execution;

await using var database = await fixture.Server.CreateDatabaseAsync();   // fixture: PostgresTestServer.StartAsync()
services.AddSharedKernelPostgres<CatalogDbContext>(
    database.BuildConfiguration(CatalogDatabase.ConnectionName),
    CatalogDatabase.ConnectionName,
    persistence => CatalogDatabase.Configure(persistence)                  // UseMultiTenancy(rowLevelSecurity: true)
);
var caller = services.AddTestRequestContext(TestRequestContext.ForTenant(Contoso));
// ...build the provider, then in a scope:
await database.CreateSchemaAsync(context);          // as the migrator role, like a migration
await database.EnableRowLevelSecurityAsync(context); // the tenant policies: never skip them
// write as Contoso; set caller.TenantId = Fabrikam; a raw-SQL count inside
// IUnitOfWork.ExecuteInTransactionAsync must be 0
```

   Never turn row-level security off in a test to save a setup line: the test then proves nothing about isolation.
   Gate the class behind an environment variable (the reference platform uses `SHOP_POSTGRES=1`): it needs Docker.

6. **Configuration.** Unit tests read no configuration: build the options in code or let the fakes default.

## Test it

Run `dotnet test`. A pipeline test that passes with no `[RequirePermission]` caller set up is a sign the request has
no permission declared: add the forbidden-caller case to every use case.

## Check

- The harness runs the same host-start seam check as production: a `With…()` opt-in with no fake registered for
  its seam fails `Build<T>()`.
- The analyzers run on test projects too; `SK0001` flags `DateTime.UtcNow` in a test (use `FakeClock`).
- `FakeRepository` cannot show a rollback or row-level security; prove those in the container tests.

## Read next

`SharedKernel.Testing`, `SharedKernel.Application.Testing`, `SharedKernel.Persistence.Testing`,
`SharedKernel.Messaging.Testing`, `SharedKernel.ArchitectureTests`, and the `.Testing` package of each capability.
