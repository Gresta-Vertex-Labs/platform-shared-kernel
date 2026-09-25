# BillingApi — sample service on the persistence stack

A multi-tenant billing API built only from the **packed** SharedKernel packages, running against PostgreSQL with
the production role split. It exists to answer one question before the persistence packages are published: does
everything work together, through an HTTP API, the way a real service would use it?

| Package | What the sample exercises |
| --- | --- |
| `SharedKernel.Persistence.EfCore` | `AddSharedKernelPostgres<BillingDbContext>("billing", …)`, conventions (strongly-typed ids, `Money` on a root and a child entity, audit and soft-delete columns, `xmin`), open-generic repositories, specifications, offset and keyset paging, `ETag`/`If-Match` with `EntityVersion`, bulk update, domain events inside the save, `MigrateOnStartup` + a seeder, tenant filter + write guard + **row-level security** on every tenant table (children included), `[TenantShared]` reference data, the design-time factory for `dotnet ef` |
| `SharedKernel.Persistence.EfCore.Encryption` | encrypted `Email` (with a case- and space-insensitive blind index) and `TaxNumber`, **per-tenant data keys**, tenant erasure (crypto-shredding), the maintenance job (`VerifyOnly`) |
| `SharedKernel.Persistence.EfCore.Auditing` | every command audited (`Succeeded` inside its transaction, `Failed` after rollback), sealed by a background sealer on **its own database role**, chain verification |
| `SharedKernel.Persistence.Dapper` | a payment written by Dapper and an aggregate changed by EF Core **in one transaction**, a report with no tenant predicate (row-level security scopes it), a back-office report across tenants on the cross-tenant role |
| `SharedKernel.Persistence.Npgsql` | one configuration shape (`ConnectionStrings:billing` + `SharedKernel:Persistence:billing`), the four canonical roles, TLS policy, the RLS privilege check |
| `SharedKernel.Persistence.Testing` | the end-to-end tests run against `PostgresTestServer` (Testcontainers, the same role split); a handler unit test over `FakeRepository`/`FakeUnitOfWork` with `TransientFailures` |
| `SharedKernel.Application[.Pipeline, .Mediator.MediatR]` | the kernel pipeline (MediatR behind `ISender`, `AddSharedKernelMediatR`) with authorization, transaction and auditing behaviors |
| `SharedKernel.ServiceDefaults[.Security, .Persistence]` | `AddSharedKernelRequestContext()` over `IUserContext`, `UseSharedKernelRequestContext()` first in the pipeline (correlation id), the database readiness check plus `AddSharedKernelReadiness()` for every provider probe, the startup gate |
| `SharedKernel.Presentation.WebApi` | `Result` → RFC 9457 ProblemDetails, the exception handler |

## Run it

The sample consumes packed packages from `../../nupkgs` (see [samples/README.md](../README.md)), so pack first:

```bash
dotnet pack Platform.SharedKernel.slnx -c Release -o nupkgs      # from the repository root
cd samples/BillingApi
```

**Everything in Docker** — PostgreSQL with the four roles plus the API, in the Production environment:

```bash
dotnet publish BillingApi.csproj -c Release -t:PublishContainer -p:ContainerRepository=billing-api -p:ContainerImageTag=local
docker compose up -d                  # http://localhost:8080
./smoke-test.sh                       # 28 checks over HTTP; exits 1 on the first failure
docker compose down -v
```

**The API on your machine**, the database in Docker:

```bash
docker compose up -d db               # PostgreSQL 17 on port 5433, roles from docker/init-roles.sql
dotnet run                            # http://localhost:5280, appsettings.Development.json
BASE_URL=http://localhost:5280 ./smoke-test.sh
```

**The tests** (Docker must be running; each run starts its own PostgreSQL):

```bash
dotnet test BillingApi.Tests
```

## Calling it

Authentication is a **development-only** header scheme (`Security/DemoAuthentication.cs`) so the sample needs no
identity provider; everything downstream of `IUserContext` is production code. A real service registers
`AddOidcAuthentication(configuration)` instead.

```bash
T=6f1c2a8e-0000-4000-8000-000000000001
H=(-H "X-Demo-User: alice" -H "X-Demo-Tenant: $T" -H "X-Demo-Permissions: billing.read,billing.write")

curl -X POST localhost:8080/customers "${H[@]}" -H 'Content-Type: application/json' \
     -d '{"name":"Ada Lovelace","email":"ada@example.com","taxNumber":"TR-1234"}'          # 201 {"id":...}
curl "localhost:8080/customers?email=ADA@example.com" "${H[@]}"                              # found through the blind index
curl -i localhost:8080/customers/{id} "${H[@]}"                                              # ETag: "766"
curl -X PUT localhost:8080/customers/{id}/name "${H[@]}" -H 'If-Match: "766"' \
     -H 'Content-Type: application/json' -d '{"name":"Ada King"}'                            # 200, new ETag; stale → 412
```

| Endpoint | Permission | Shows |
| --- | --- | --- |
| `POST /customers`, `GET /customers/{id}`, `GET /customers?email=`, `PUT /customers/{id}/name`, `DELETE /customers/{id}` | write / read | encryption, blind index, ETag, soft delete |
| `POST /invoices`, `GET /invoices/{id}`, `GET /invoices?page=&pageSize=&status=`, `GET /invoices/browse?cursor=&limit=` | write / read | `Money`, child entities, offset and keyset paging |
| `POST /invoices/{id}/issue`, `POST /invoices/{id}/payments`, `POST /invoices/expire-drafts` | write | domain events, Dapper + EF Core in one transaction, bulk update |
| `GET /reports/revenue` | read | Dapper under row-level security |
| `GET /audit/{type}/{id}`, `GET /audit/{type}` | read | audit history, chain verification |
| `GET /admin/reports/revenue-by-tenant`, `POST /admin/tenants/{id}/erase` | admin | cross-tenant role, crypto-shredding |
| `GET /health/ready`, `GET /health/live` | — | readiness: migrations, key ring, audit sealer |

## Where to look

| File | What it shows |
| --- | --- |
| `Program.cs` | the whole composition, one registration per concern |
| `Infrastructure/BillingDbContext.cs` | the context, the only configuration conventions cannot know, `BillingDatabase.Configure` shared by `Program.cs` and the design-time factory |
| `Infrastructure/Migrations/*_Initial.cs` | the generated migration plus the platform objects: RLS for the whole model, a Dapper-only table with its own policy, the audit ledger with a sealer role, the tenant key table |
| `docker/init-roles.sql` | the canonical role script, as-is |
| `Application/Invoices.cs` | `PayInvoiceHandler`: a Dapper session joining the command's transaction |
| `Application/Reports.cs` | SQL with no tenant predicate; the cross-tenant scope |
| `BillingApi.Tests/` | 15 end-to-end tests over HTTP in the Production environment, one unit test over the fakes |

## Adding a migration

```bash
docker compose up -d db
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add <Name> -o Infrastructure/Migrations
```

`BillingDbContextFactory` connects as `app_migrator` and applies the same capabilities as `Program.cs`
(`BillingDatabase.Configure`), so the migration sees the encrypted columns' widths and blind-index columns. A new
tenant table needs `migrationBuilder.EnableTenantRowLevelSecurity("table")` in that migration; the startup check
refuses to start in Production otherwise.
