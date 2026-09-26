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
| `SharedKernel.Application[.Pipeline, .Mediator.MediatR]` | one call, `AddSharedKernelApplication(typeof(Program).Assembly, app => app.UseMediatR().WithTransactions().WithAuditing())`: the handlers of the assembly behind the kernel's `ISender` (MediatR is the adapter, referenced only here), `[RequirePermission]` on every command and query (always enforced), one retry-safe transaction per command, an audit record per auditable command |
| `SharedKernel.ServiceDefaults[.Security, .Persistence]` | `AddSharedKernelRequestContext()` over `IUserContext`, `UseSharedKernelRequestContext()` first in the pipeline (correlation id and the request's context), the database readiness check plus `AddSharedKernelReadiness()` for every provider probe, the startup gate |
| `SharedKernel.Presentation.WebApi` | `AddSharedKernelWebApi()` + `UseSharedKernelWebApi()` right after the request context; typed results (`ToOk`, `ToCreated`, `ToNoContent`, `ToOkWithETag`); an `IfMatch<EntityVersion>` handler parameter (`ETag`, 304, 428, 400, 412); endpoint modules (`IEndpointModule`, mapped by the generated `app.MapEndpoints()`); `Paging`/`CursorPaging` parameters; every error an RFC 9457 problem |

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
./smoke-test.sh                       # 34 checks over HTTP; exits 1 on the first failure
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
curl -i localhost:8080/customers/{id} "${H[@]}"                                              # ETag: "AdU2PjcYmR4Kx0aB9wFtLq3zVe8H"
curl -i localhost:8080/customers/{id} "${H[@]}" -H 'If-None-Match: "AdU2PjcYmR4Kx0aB9wFtLq3zVe8H"'   # 304, no body
curl -X PUT localhost:8080/customers/{id}/name "${H[@]}" -H 'If-Match: "AdU2PjcYmR4Kx0aB9wFtLq3zVe8H"' \
     -H 'Content-Type: application/json' -d '{"name":"Ada King"}'                            # 200, new ETag; no If-Match → 428, stale → 412
curl -X DELETE localhost:8080/customers/{id} "${H[@]}" -H 'If-Match: "Ae0rT7…"'              # 204 — deletes name their version too
```

| Endpoint | Permission | Shows |
| --- | --- | --- |
| `POST /customers`, `GET /customers/{id}`, `GET /customers?email=`, `PUT /customers/{id}/name`, `DELETE /customers/{id}` | write / read | encryption, blind index, `ETag`/`If-Match` (304, 428, 400, 412), soft delete |
| `POST /invoices`, `GET /invoices/{id}`, `GET /invoices?page=&pageSize=&status=`, `GET /invoices/browse?cursor=&limit=` | write / read | `Money`, child entities, offset and keyset paging |
| `POST /invoices/{id}/issue`, `POST /invoices/{id}/payments`, `POST /invoices/expire-drafts` | write | domain events, Dapper + EF Core in one transaction, bulk update |
| `GET /reports/revenue` | read | Dapper under row-level security |
| `GET /audit/{type}/{id}`, `GET /audit/{type}` | read | audit history, chain verification |
| `GET /admin/reports/revenue-by-tenant`, `POST /admin/tenants/{id}/erase` | admin | cross-tenant role, crypto-shredding |
| `GET /health/ready`, `GET /health/live` | — | readiness: migrations, key ring, audit sealer |

Permissions are declared once, where the work is: each command and query states its own with `[RequirePermission]`,
and the pipeline's authorization behavior checks it before the handler runs — on every path the use case can take,
HTTP or otherwise — so no endpoint repeats it. The tenant erasure is a command like any other (`EraseTenant`,
`[RequirePermission(Permissions.Admin)]`). An anonymous caller gets 401 and a caller without the permission 403, both as
problems:

```csharp
[RequirePermission(Permissions.Write)]
public sealed record RegisterCustomer(CustomerId Id, string Name, string Email, string? TaxNumber)
    : ICommand<CustomerId>, IAuditableRequest<Result<CustomerId>> { … }
```

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"Forbidden","status":403,
 "detail":"You are not permitted to perform this operation.","instance":"/admin/tenants/…/erase",
 "errorCode":"forbidden.insufficient_permission","correlationId":"6a6a01d5…","traceId":"00-6a6a01d5…-01"}
```

## Optimistic concurrency over HTTP

The customer's version travels as its `ETag`, and changes require it back. It is an `EntityVersion`: PostgreSQL's
`xmin` sealed with the customer's identity under a subkey of the service's key provider (registered in `Program.cs`), so
the ETag never shows the database's transaction counter. A change declares an `IfMatch<EntityVersion>` parameter:

```csharp
customers.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
    sender.Send(new GetCustomer(new CustomerId(id)), ct)
        .ToOkWithETag(found => found.Version.ToString(), found => found.Customer));   // ETag; If-None-Match → 304

customers.MapPut("/{id:guid}/name", (Guid id, RenameCustomerRequest body, IfMatch<EntityVersion> ifMatch, ISender sender, CancellationToken ct) =>
    sender.Send(new RenameCustomer(new CustomerId(id), body.Name, ifMatch.Version), ct)   // always a parsed version
        .Bind(() => sender.Send(new GetCustomer(new CustomerId(id)), ct))
        .ToOkWithETag(found => found.Version.ToString(), found => found.Customer));
```

The parameter requires the header, and `UseSharedKernelWebApi()` checks it before the handler runs (RFC 9110
section 13.1.1), so the handler never parses a header itself:

| `If-Match` | Answer | `errorCode` |
| --- | --- | --- |
| missing, or `*` (it names no version) | 428 | `precondition.required` |
| malformed, or more than one tag | 400 | `precondition.invalid` |
| a weak tag, or a tag that is not an `EntityVersion` (a plain number such as the raw `xmin`) | 412 | `precondition.failed` |
| a version that is not current: another writer saved first, it is another customer's, or it was altered | 412 | `persistence.concurrency_conflict` |

Nothing catches the `ConflictException` the save throws in the last case. It is a `Conflict` with the stale-version
code, and the request named its version in `If-Match`, so it is answered 412, keeping its code; every other conflict
(a duplicate email, say) stays 409. The client reads the customer again for the current `ETag` and retries.

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.13","title":"Precondition Failed","status":412,
 "detail":"'Customer' was changed or deleted by someone else. Reload it and retry.",
 "instance":"/customers/…/name","errorCode":"persistence.concurrency_conflict","correlationId":"179b8259…","traceId":"00-179b8259…-01"}
```

## Where to look

| File | What it shows |
| --- | --- |
| `Program.cs` | the whole composition, one registration per concern |
| `Api/*Endpoints.cs` | the HTTP surface, one endpoint module per area (customers, invoices, reports and audit, administration): each endpoint sends a command or query through `ISender` and maps the `Result` with a typed result; `IfMatch<EntityVersion>` + `ToOkWithETag()`; `Paging`/`CursorPaging` |
| `Features/<Area>/<UseCase>.cs` | one use case per file: the command or query with its `[RequirePermission]`, and its handler |
| `Infrastructure/BillingDbContext.cs` | the context, the only configuration conventions cannot know, `BillingDatabase.Configure` shared by `Program.cs` and the design-time factory |
| `Infrastructure/Migrations/*_Initial.cs` | the generated migration plus the platform objects: RLS for the whole model, a Dapper-only table with its own policy, the audit ledger with a sealer role, the tenant key table |
| `docker/init-roles.sql` | the canonical role script, as-is |
| `Features/Invoices/PayInvoice.cs` | `PayInvoiceHandler`: a Dapper session joining the command's transaction |
| `Features/Reports/GetRevenue.cs`, `GetRevenueByTenant.cs` | SQL with no tenant predicate; the cross-tenant scope |
| `Features/Tenants/EraseTenant.cs` | crypto-shredding a tenant inside a cross-tenant scope |
| `BillingApi.Tests/` | 16 end-to-end tests over HTTP in the Production environment, one unit test over the fakes |

## Adding a migration

```bash
docker compose up -d db
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add <Name> -o Infrastructure/Migrations
```

`BillingDbContextFactory` connects as `app_migrator` and applies the same capabilities as `Program.cs`
(`BillingDatabase.Configure`), so the migration sees the encrypted columns' widths and blind-index columns. A new
tenant table needs `migrationBuilder.EnableTenantRowLevelSecurity("table")` in that migration; the startup check
refuses to start in Production otherwise.
