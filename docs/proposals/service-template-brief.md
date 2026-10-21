# Build brief: `platform-service-template`

> **For the agent.** This brief is your work order for building the `platform-service-template` repository
> from an empty repo. Read all of it before you start, then work through the phases in section 10 in order.
> Each phase ends with acceptance checks you must run and pass. After each phase, commit it. **Stop at
> every checkpoint** (marked ⏸) and wait for the user's review. Where this brief says "ask", ask; do not
> guess.

---

## 1. What we are building and why

**Platform.SharedKernel** is a set of 104 NuGet packages that every microservice on this platform builds
on. They cover persistence, messaging, security, hosting, testing and more. They are published to GitHub
Packages from `Gresta-Vertex-Labs/platform-shared-kernel`, all at one version.

This repo produces a **`dotnet new` template**. One command creates a new service repository that
already has:
- the platform's project shape
- the kernel pinned at one version
- authentication, multi-tenancy, PostgreSQL with row-level security, and RabbitMQ with the outbox, all
  wired
- guardrails: analyzers as errors and architecture tests
- tests of every kind
- local development through Aspire
- CI/CD and a container image
- the AI layer: the `sharedkernel` Claude Code skill and a `CLAUDE.md`

```bash
dotnet new install Platform.ServiceTemplate
dotnet new sk-service -n Shipping --aggregate Shipment
```

**Why it exists:** every service starts with the same decisions made correctly, so neither a developer
nor an AI assistant has to rebuild, or quietly get wrong, the foundation. Everything a service adds later
(caching, storage, jobs, …) comes through the `sharedkernel` skill's recipes, not through the template.

**The one fact that shapes every decision:** `dotnet new` copies files **once**. Template improvements
never reach services that already exist. So:
- Anything that must keep evolving is **pulled in by version**, not copied: kernel packages, the
  analyzers' rules, the Linter's `.editorconfig`, and the skill.
- What the template copies is kept to the service's own skeleton.

## 2. Fixed decisions (do not revisit)

| Topic | Decision |
| --- | --- |
| Template package id | `Platform.ServiceTemplate`, published to the same GitHub Packages feed as the kernel |
| Short name | `sk-service` |
| Rename token (`sourceName`) | `Platform.ServiceTemplate`. It must never be a word that appears naturally (`Service`, `Template`). |
| Kernel version | `SharedKernelVersion` = **`1.0.0-rc.6`** (newest published). One property drives every kernel package. |
| Project shape | Six `src` projects, four test projects, one AppHost (section 4) |
| Database | PostgreSQL via EF Core, **always on**. No `--database` option. |
| Tenancy | **Always multi-tenant**: tenant from the signed token claim, tenanted aggregate, `TenantedDbContext`, row-level security on. No option. |
| Messaging | **RabbitMQ with the EF Core outbox, always on.** The example publishes one integration event through the outbox. |
| Authentication | OIDC (`SharedKernel.Security.Oidc`), always on. Locally, **Keycloak** runs in the AppHost with a dev realm. |
| Local development | **Aspire AppHost**: one F5 starts the service, PostgreSQL, RabbitMQ and Keycloak |
| Deployment | **Dockerfile + CI** pushing to GitHub Container Registry. No Helm, no K8s manifests (they live in a separate platform/GitOps repo). |
| Example code | **One small working slice** (section 6) |
| Published packages of a service | `{Service}.Contracts` and `{Service}.Client`, **both on by default** |
| Template options | `-n` (service name) and `--aggregate` (example aggregate name, default `Item`). Add no others without asking. |

## 3. Sources of truth (read these, don't remember them)

The kernel's API is the authority. Names below are from the reference platform and are believed current,
but **verify every API call against the package README at the pinned version before you write it.**

| Need | Where |
| --- | --- |
| Which package for which need, which project references it | The `sharedkernel` skill: download it per section 8, then read `SKILL.md` and `recipes/*.md`. Read `recipes/new-service.md` first. |
| Exact API, configuration keys, pitfalls of a package | The README inside the restored package: `~/.nuget/packages/<package id lowercased>/1.0.0-rc.6/README.md` |
| A complete working service on these packages | The kernel repo at tag `v1.0.0-rc.6`: `samples/Shop/Ordering/` (closest to this template: PostgreSQL + RLS, RabbitMQ + outbox, OIDC, multi-tenancy) and `samples/Shop/Catalog/` |
| AppHost with PostgreSQL, RabbitMQ and Keycloak (realm import) | Kernel repo: `samples/Shop/Shop.AppHost/` (`ShopInfrastructure.cs`, `ServiceConfiguration.cs`, `keycloak/`) |
| The service-shape architecture rules | Kernel repo: `samples/Shop/Shop.TestSupport/ServiceShape.cs`. It is **not** shipped as a package; you re-implement it (section 7). |
| Third-party versions (Aspire, Testcontainers, xUnit, …) | Kernel repo: `Directory.Packages.props` at `v1.0.0-rc.6`. Use the same versions. |
| SDK version | Kernel repo `global.json`: SDK `10.0.300`, `rollForward: latestFeature` |
| Skill download contract | Kernel repo: `consumer/README.md`, section "Distribution contract" |

Read the kernel repo with the GitHub CLI (`gh api`, `gh repo clone … -- --depth 1 --branch v1.0.0-rc.6`
into a scratch folder outside this repo). **Never copy kernel-internal files** (`CLAUDE.md`, `state-map.md`,
`eng/`) into this repo.

**Prerequisites. Check them in phase 0 and stop if any is missing:**
- .NET SDK 10.0.300+
- Docker running
- `gh` authenticated with `read:packages` and read access to `Gresta-Vertex-Labs/platform-shared-kernel`
- `GITHUB_PACKAGES_TOKEN` set (`export GITHUB_PACKAGES_TOKEN=$(gh auth token)`)

## 4. Layout

### 4.1 This repository (the template's own repo)

```
platform-service-template/
├─ README.md                          for users: install, create a service, options, upgrade notes
├─ CLAUDE.md                          for maintainers of THIS repo (section 9.2); never shipped
├─ CHANGELOG.md                       one entry per template release, with manual upgrade steps
├─ Platform.ServiceTemplate.csproj    the pack project (PackageType=Template); packs content/
├─ global.json                        same SDK as the kernel
├─ NuGet.Config                       nuget.org + GitHub Packages (token from %GITHUB_PACKAGES_TOKEN%)
├─ eng/
│  └─ verify-template.sh              install the local template, generate, build, test, scan (section 11)
├─ .github/workflows/
│  ├─ ci.yml                          on PR/push: verify-template.sh for every case in section 11
│  └─ release.yml                     on tag v*: verify, pack, publish the template package
└─ content/
   └─ Platform.ServiceTemplate/       the template root: a real, buildable solution
      ├─ .template.config/template.json
      └─ … the generated service repo (4.2), with the rename tokens in place
```

`content/Platform.ServiceTemplate/` is a **normal, working solution**. You must be able to open it,
build it, run it and test it as-is, with the tokens unreplaced. Develop it as a service first and
templatize it second.

### 4.2 What a generated service repo looks like (`-n Shipping --aggregate Shipment`)

```
Shipping/
├─ README.md                        how to run, test, release this service
├─ CLAUDE.md                        short, service-specific; points to the skill (section 9.1)
├─ .claude/skills/sharedkernel/     the synced skill, committed; never edited by hand
├─ global.json  NuGet.Config  .gitignore  .gitattributes  .dockerignore  .editorconfig
├─ Directory.Build.props            analyzers + Linter on every project, nullable, warnings as errors, MinVer
├─ Directory.Packages.props         central package management; <SharedKernelVersion>1.0.0-rc.6</SharedKernelVersion>
├─ Shipping.slnx
├─ Dockerfile                       builds src/Shipping.Api
├─ eng/
│  ├─ sync-sharedkernel-skill.sh    reads SharedKernelVersion, downloads that skill, replaces the folder
│  └─ sync-sharedkernel-skill.ps1   the same for Windows PowerShell
├─ .github/workflows/
│  ├─ ci.yml                        build, format, audit, unit + architecture + integration + client tests, pack, image build
│  └─ release.yml                   on tag v*: publish Contracts + Client packages and push the image
├─ src/
│  ├─ Shipping.Domain/              internal
│  ├─ Shipping.Application/         internal
│  ├─ Shipping.Infrastructure/      internal
│  ├─ Shipping.Api/                 internal, the host
│  ├─ Shipping.Contracts/           PUBLISHED: integration events + HTTP request/response DTOs
│  └─ Shipping.Client/              PUBLISHED: typed REST client for other services
├─ tests/
│  ├─ Shipping.UnitTests/           Domain + Application with fakes, no Docker
│  ├─ Shipping.IntegrationTests/    Api + Infrastructure on real PostgreSQL and RabbitMQ (Testcontainers)
│  ├─ Shipping.ArchitectureTests/   shape rules (section 7) + kernel ArchitectureTests rules
│  └─ Shipping.Client.Tests/        the published client against the real API, in-process
└─ dev/
   └─ Shipping.AppHost/             Aspire: service + PostgreSQL + RabbitMQ + Keycloak (realm json)
```

**Dependencies between projects** (arrows point at what a project references):

```
              Domain
                 ▲
Contracts ◄── Application ◄── Infrastructure
   ▲                ▲               ▲
   │                └───── Api ─────┘
 Client
```

- Domain references no project.
- Contracts references no project, and Domain and Contracts never reference each other. The Api and
  Application map between them.
- Application → Domain, Contracts.
- Infrastructure → Application.
- Api → Application, Infrastructure.
- Client → Contracts only.
- Nothing references Api.
- Tests and the AppHost reference what they need.

Only **Contracts and Client** are packable. Every other project sets `IsPackable=false`.

## 5. Packages per project

All versions come from `SharedKernelVersion` through central package management. Foundation packages
(`Primitives`, `Execution`, `Core`) come in transitively; do not list them.

| Project | SharedKernel packages | Purpose |
| --- | --- | --- |
| **Every project** (`Directory.Build.props`, `PrivateAssets="all"`) | `SharedKernel.Analyzers`, `SharedKernel.Linter` | Conventions as compiler diagnostics; CSharpier format check + shared `.editorconfig` |
| Domain | `SharedKernel.Domain` | Aggregates, value objects, ids, `TenantedAggregateRoot<TId>`, domain events |
| Contracts | `SharedKernel.Contracts` | `IIntegrationEvent`, `[IntegrationEvent]`, paging DTOs. **Nothing else.** |
| Application | `SharedKernel.Application`, `SharedKernel.Persistence.Abstractions`, `SharedKernel.Messaging.Abstractions` | Commands/queries, `ISender`, `IRequestValidator<T>`, `[RequirePermission]`, `IRepository<T,TId>`, `IEventPublisher` |
| Infrastructure | `SharedKernel.Persistence.EfCore`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Messaging.MassTransit.RabbitMq`, `SharedKernel.Messaging.MassTransit.EfCore` | EF Core on PostgreSQL (brings `Persistence.Npgsql`), MassTransit on RabbitMQ, the outbox |
| Api | `SharedKernel.ServiceDefaults`, `SharedKernel.ServiceDefaults.Security`, `SharedKernel.ServiceDefaults.Persistence`, `SharedKernel.MultiTenancy`, `SharedKernel.Security.Oidc`, `SharedKernel.Application.Pipeline`, `SharedKernel.Application.Mediator.MediatR`, `SharedKernel.Presentation.WebApi`, `SharedKernel.Presentation.OpenApi` | Telemetry/health, request context, DB readiness, tenant resolution, authentication, pipeline, endpoints, OpenAPI + versioning |
| Client | `SharedKernel.Communication.Rest` | Typed client returning `Result<T>`, with the kernel's resilience, discovery and context propagation. **Nothing else** besides Contracts. |
| UnitTests | `SharedKernel.Testing`, `SharedKernel.Application.Testing`, `SharedKernel.Persistence.Testing`, `SharedKernel.Messaging.Testing` | Fakes: `TestRequestContext`, `ApplicationPipelineTestHarness`, `FakeRepository`, in-memory bus |
| IntegrationTests | `SharedKernel.Testing`, `SharedKernel.Persistence.Testing`, `SharedKernel.Presentation.Testing` | `PostgresTestServer` (RLS **on**, via `EnableRowLevelSecurityAsync`), endpoint testing helpers |
| ArchitectureTests | `SharedKernel.ArchitectureTests` | Kernel rules (domain purity, forbidden patterns) + your shape rules |
| Client.Tests | `SharedKernel.Testing`, `SharedKernel.Presentation.Testing`, `SharedKernel.Communication.Testing` if its README fits | The client against the API in `WebApplicationFactory` |

Not in the template: Caching, Idempotency, Storage, Search, AI, Reporting, Workflows, Scheduling,
Integration, other Security schemes, gRPC/SignalR/GraphQL, Key Vault, Localization, FeatureManagement,
FluentValidation, and the EF Core Auditing/Encryption packages. A service adds them later through the
skill's recipes.

**Analyzer severity:** `SharedKernel.Analyzers` ships every rule at Warning. Ship a `.globalconfig`, or a
section in `.editorconfig`, that raises **every SK rule to `error`** except the advisory SK0034. Take the
rule list from the Analyzers README at the pinned version. Also set `TreatWarningsAsErrors=true` in
`Directory.Build.props`. If the Linter package already ships the shared `.editorconfig`, consume it
the way its README says instead of copying its contents.

## 6. The example slice

The slice exists to give developers and the AI **one correct pattern of each kind to copy**. Keep it
small and deletable. The tokens `TemplateItem` / `templateItem` / `template_item` / `template-item` are
replaced by `--aggregate` (section 8.2). The text below uses the generated names for `-n Shipping
--aggregate Shipment`.

**Domain**
- `Shipment : TenantedAggregateRoot<ShipmentId>` with a strongly-typed id, one or two properties (for
  example `Reference`, `CreatedAt`), a factory `Create(...)` returning `Result<Shipment>`, one business
  rule, and one domain event `ShipmentCreated`.

**Contracts**
- `Shipping.Contracts.Events.ShipmentCreatedIntegrationEvent`: a `sealed record` implementing
  `IIntegrationEvent`, with `[IntegrationEvent("shipping.shipment_created", Version = 1)]`. The wire name
  is lowercase `{service}.{aggregate_snake}_created`.
- `Shipping.Contracts.Api.CreateShipmentRequest`, `ShipmentResponse`. Plain records, no domain types.

**Application**
- `CreateShipmentCommand : ICommand<ShipmentId>` with `[RequirePermission("shipping.shipments.write")]`, a
  validator (`IRequestValidator<T>`), and a handler that:
  1. creates the aggregate
  2. saves it through `IRepository`
  3. publishes `ShipmentCreatedIntegrationEvent` through `IEventPublisher`, so the outbox writes it in
     the same transaction
- `GetShipmentQuery : IQuery<ShipmentResponse>` with `[RequirePermission("shipping.shipments.read")]`.
  Map the aggregate to the Contracts DTO here or in the Api; choose one place and be consistent.
- Expected failures are `Result` errors (`NotFound`, `Validation`), never exceptions.

**Infrastructure**
- `ShippingDbContext : TenantedDbContext`, the entity configuration, **one initial migration**, row-level
  security on.
- `AddShippingInfrastructure(this IHostApplicationBuilder)` registers:
  - `AddSharedKernelPostgres<ShippingDbContext>("shipping", p => p.UseMultiTenancy(rowLevelSecurity: true).MigrateOnStartup())`
  - `AddSharedKernelMessaging(configuration).UseRabbitMq(…).WithEntityFrameworkOutbox<ShippingDbContext>()…`

  Verify the exact shape against the READMEs and Shop's `OrderingInfrastructure.cs`.

**Api.** `Program.cs` follows the reference platform's order. This sketch is from Shop Ordering; verify
every call against the READMEs:

```csharp
builder.AddServiceDefaults();
builder.WithPersistenceTelemetry();
builder.WithMessagingTelemetry();
builder.Services.AddOidcAuthentication(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddSharedKernelRequestContext();
builder.Services.AddSharedKernelMultiTenancy(o => builder.Configuration.GetSection(TenantResolutionOptions.SectionName).Bind(o));
builder.AddShippingInfrastructure();
builder.Services.AddSharedKernelApplication(typeof(CreateShipmentCommand).Assembly, app => app.UseMediatR().WithTransactions());
builder.Services.AddHealthChecks().AddDatabaseReadinessCheck<ShippingDbContext>().AddSharedKernelReadiness();
builder.AddSharedKernelWebApi();
builder.Services.AddSharedKernelOpenApi();          // check the README for the exact registration

var app = builder.Build();
app.UseSharedKernelRequestContext();                 // first middleware
app.UseSharedKernelWebApi(p => p.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()));
app.MapDefaultHealthCheckEndpoints();
app.MapSharedKernelOpenApi();
app.MapEndpoints();
app.Lifetime.ApplicationStarted.Register(() => app.Services.GetRequiredService<StartupGate>().MarkReady());
await app.RunAsync();

public partial class Program;                        // for WebApplicationFactory
```

- One `IEndpointModule`, `ShipmentEndpoints`, with `POST /v1/shipments` → 201 Created and
  `GET /v1/shipments/{id}` → 200 or a ProblemDetails 404. Both send through `ISender`, and both use the
  Contracts DTOs as the request and response bodies. Use the kernel's API versioning (OpenApi README),
  not hand-written `/v1` strings, if it supports that.
- `appsettings.json`: only the sections these packages need, with the section names from their READMEs.
  No secrets. Development values come from the AppHost.

**Client**
- `IShippingClient` with `CreateShipmentAsync(CreateShipmentRequest, CancellationToken)` →
  `Result<ShipmentId or Guid>`, and `GetShipmentAsync(Guid, CancellationToken)` → `Result<ShipmentResponse>`.
- `ShippingClient` implemented over `SharedKernel.Communication.Rest` (`GetResultAsync` /
  `PostResultAsync`).
- A registration extension for consumers, for example
  `AddShippingClient(this <communication builder>, string name = "shipping")`, wrapping
  `AddRestClient<IShippingClient, ShippingClient>(name)`.
- The client's README (packed) explains: register it in **your Infrastructure project**, behind **your
  own** port interface; never reference it from an Application project.

**Tests** (one of each kind, all passing)

| Project | Test |
| --- | --- |
| UnitTests | `Shipment.Create` rule (Domain); `CreateShipmentCommand` through `ApplicationPipelineTestHarness` with `TestRequestContext`, `FakeRepository` and the in-memory publisher, asserting the event was published |
| IntegrationTests | `POST` then `GET` through `WebApplicationFactory` on Testcontainers PostgreSQL and RabbitMQ, with RLS on. A second test proves **tenant B cannot read tenant A's shipment**. A third proves the integration event leaves through the outbox. |
| ArchitectureTests | Every rule in section 7 |
| Client.Tests | `ShippingClient` against the in-process API: create, then get; a 404 maps to a `NotFound` `Result` |

Use `SharedKernel.Testing.Internal`-style fixtures only if a package exposes them. Testcontainers images
match the versions Shop uses.

## 7. Architecture rules

Re-implement the service-shape rules of the kernel's `samples/Shop/Shop.TestSupport/ServiceShape.cs`
(about 11 rules), adapted to the six-project shape, in `tests/Shipping.ArchitectureTests`. Add these:

1. Domain references no project; its kernel packages are Foundation/Model only.
2. Contracts references no project; its only kernel package is `SharedKernel.Contracts`. **Domain ↮
   Contracts.**
3. Application references only Domain and Contracts; no Adapter- or Host-tier package; no MediatR.
4. Infrastructure references only Application; its kernel packages are Adapter- or Foundation-tier.
5. Api references Application and Infrastructure; its direct kernel packages are Host-tier.
6. Client references only Contracts; its only kernel package is `SharedKernel.Communication.Rest`.
7. No project references Api. No production project references a Testing-tier package.
8. Contracts types expose no Domain types (check by type, not only by reference).
9. Plus the relevant rules from `SharedKernel.ArchitectureTests` (domain purity and so on), as its README
   shows.

Determine a kernel package's tier from the package itself (its README's tier, or a small index) the way
Shop's `KernelPackageIndex` does. Hard-code nothing that changes when the kernel adds a package.

> Kernel follow-up (not your job, note it in this repo's README "Known gaps"): these rules belong in
> `SharedKernel.ArchitectureTests` so every service gets them by version instead of by copy.

## 8. Template mechanics

### 8.1 `template.json`

- `identity` `Platform.ServiceTemplate`, `shortName` `sk-service`, `sourceName` `Platform.ServiceTemplate`,
  `preferNameDirectory: true`, `classifications` `["Web","Microservice","SharedKernel"]`, `tags`
  `{ "language": "C#", "type": "solution" }`.
- Exclude `bin/`, `obj/`, `.vs/`, `*.user`, `TestResults/` and any local secrets.
- Post action: `dotnet restore`. Do **not** run git or the skill sync as post actions; the README says to
  run them.

### 8.2 Tokens and forms

Content uses these tokens. Each must be replaced everywhere it appears: file and folder names, namespaces,
strings, JSON and YAML.

| Token in content | Replaced by | Example (`Shipping`, `Shipment`) | Used for |
| --- | --- | --- | --- |
| `Platform.ServiceTemplate` | the `-n` name | `Shipping` | Projects, namespaces, solution, package ids |
| `platformservicetemplate` | name, lowercase, dots removed | `shipping` | Event wire prefix, DB name, Aspire resources, Keycloak client, permission prefix |
| `platform-servicetemplate` | name, kebab-case | `shipping` | Container image name, URLs |
| `TemplateItem` | `--aggregate` | `Shipment` | Types and files |
| `templateItem` | aggregate camelCase | `shipment` | Variables |
| `template_item` | aggregate snake_case | `shipment` | Event wire name, table name |
| `template-items` | aggregate kebab, plural | `shipments` | Route segment |

- Use derived symbols (`valueTransform`) for the case forms.
- Pluralization: default to aggregate + `s`. Add an optional `--aggregate-plural` override only if it
  stays simple (a coalesce generator). Ask if not.
- Validate that `-n` is a valid C# namespace and `--aggregate` a valid identifier.
- If you find you need a token that isn't in this table, ask before adding it.

### 8.3 Skill sync

`eng/sync-sharedkernel-skill.sh` and `.ps1` (PowerShell 5.1-compatible; no `&&`, no `??`) must:
1. Read `SharedKernelVersion` from `Directory.Packages.props`.
2. Download that version's skill exactly as the kernel's distribution contract says:
   `gh release download v<version> --repo Gresta-Vertex-Labs/platform-shared-kernel --pattern 'sharedkernel-skill-v*.zip'`.
3. Replace `.claude/skills/sharedkernel/` **as a whole**: delete the old folder, unzip the new one.
4. Verify that `SKILL.md` contains the version and no `{{`, then print what changed (`git diff --stat`
   if inside a repo).
5. Fail clearly when `gh` is missing or unauthenticated, or the release or asset does not exist.

The template's content ships the skill **already synced** at `1.0.0-rc.6`, so a fresh service works
before anyone runs the script. Upgrading the kernel in a service = bump `SharedKernelVersion`, run the
sync, build, commit both.

## 9. The two `CLAUDE.md` files. Never mix them up.

### 9.1 The shipped one (`content/…/CLAUDE.md`, becomes the service's)

Short, about 40–60 lines, service-specific only:
- What this service is (a placeholder section the team fills in): bounded context, aggregates, events
  published and consumed, synchronous dependencies.
- "Built on Platform.SharedKernel; the version is `SharedKernelVersion` in `Directory.Packages.props`.
  For any infrastructure or cross-cutting need, **use the `sharedkernel` skill first**; never hand-roll
  what it covers. Never edit `.claude/skills/sharedkernel/`; run `eng/sync-sharedkernel-skill.*` after a
  version bump."
- The project map (section 4.2), the dependency rules (section 7, in prose), and "Contracts and Client
  are published: additive changes only; a breaking change is a new event version or a new API version."
- How to build, test (which test project needs Docker), run (AppHost) and format.
- Where integration events are catalogued (platform-docs).

No kernel internals, domain ids or maintainer vocabulary.

### 9.2 This repo's own `CLAUDE.md` (for maintainers of the template)

Write it in phase 0 and keep it current:
- what this repo is
- the layout (section 4.1)
- "content/ is a working solution; develop there, then verify through `eng/verify-template.sh`"
- the token table
- the kernel version bump procedure (section 12)
- "never edit the synced skill by hand"
- the CHANGELOG rule (every release lists manual upgrade steps for existing services)
- the commit convention (Conventional Commits)

## 10. Phases

Every phase: the acceptance checks pass, then **one or more Conventional Commits** (`feat:`, `build:`,
`ci:`, `docs:`, `test:`). Never push or tag unless a phase says to. Never skip hooks.

### Phase 0: Bootstrap
- Check the prerequisites in section 3 and report versions. Stop if any is missing.
- Clone the kernel at `v1.0.0-rc.6` into a scratch folder outside this repo for reading.
- Create `README.md` (stub), `CLAUDE.md` (9.2), `CHANGELOG.md`, `.gitignore`, `.gitattributes` (LF),
  `global.json`, `NuGet.Config`.
- **Ask the user** the open questions in section 13 before phase 1.
- ✅ The repo has the files; the prerequisites are reported.

### Phase 1: Solution skeleton (no example code yet)
- In `content/Platform.ServiceTemplate/`: the build files, the six `src` projects with the references in
  section 4.2, the four test projects (empty but compiling), the AppHost (empty), and the `.slnx`.
- Packages per section 5. Analyzers raised to error; `TreatWarningsAsErrors`.
- `Program.cs` wiring per section 6, without the slice.
- ✅ `dotnet build` passes with **0 warnings, 0 errors**. The Linter's format check passes.

### Phase 2: Example slice
- Everything in section 6 except tests: domain, contracts, application, infrastructure with the
  migration, endpoints, client.
- ✅ The build is clean. `dotnet ef migrations` produces no pending changes.

### Phase 3: Tests and architecture rules
- Section 6 tests and section 7 rules.
- ✅ All four test projects pass. Integration tests pass on Testcontainers. Deliberately break one shape
  rule (for example, reference Api from Application), confirm the architecture test fails, then revert.

### Phase 4: Local development
- AppHost: PostgreSQL, RabbitMQ (management UI), and Keycloak with an imported dev realm. The realm has:
  - a client for the service
  - the tenant claim the kernel's tenant resolution reads (check the MultiTenancy README)
  - permission claims for `…write`/`…read`
  - two test users in two different tenants

  Model it on Shop's AppHost and realm.
- Wire the service with `WaitFor` on its dependencies; configuration flows from the AppHost.
- The README's "Run locally" section: start, get a token for each test user (a `curl` example), call
  both endpoints.
- ✅ The AppHost starts; `/health/ready` turns healthy after migrations. As user A, create and get a
  shipment. As user B, get returns 404. The event is visible in RabbitMQ.
- ⏸ **Checkpoint 1.** Stop and report: how to run it, what you verified, and anything you had to decide
  that this brief did not settle.

### Phase 5: AI layer
- The sync scripts (8.3). Run the sync to commit the `1.0.0-rc.6` skill into the content.
- The shipped `CLAUDE.md` (9.1).
- ✅ The skill folder matches the release zip exactly; the `.sh` and `.ps1` both work (run both). The
  `CLAUDE.md` reads correctly for a service named `Platform.ServiceTemplate`, and so will after renaming.

### Phase 6: Service CI/CD, Dockerfile, packaging
- **Dockerfile:**
  - multi-stage, publishing `src/{Service}.Api`
  - the feed token passed as a **BuildKit secret** (`--mount=type=secret`), never a build arg or a layer
  - runtime on the ASP.NET 10 chiseled (non-root) image
  - exposes the port the README documents
  - its health endpoints match `MapDefaultHealthCheckEndpoints()` (liveness and readiness paths from the
    ServiceDefaults README)
- **Versioning:** MinVer from git tags (`v*`), the same tool as the kernel. No `<Version>` in any
  `.csproj`.
- **Contracts and Client packaging:**
  - packable, with a packed README each (the Client's says "Infrastructure only, behind your own port")
  - `EnablePackageValidation=true`; `PackageValidationBaselineVersion` is left empty until the service's
    first release
  - the service README explains setting the baseline after the first release, so the build then fails
    on any breaking change
- **`ci.yml`** (on PR and push):
  - restore with the feed token
  - format check (Linter)
  - build
  - vulnerable-package audit failing on any NU190x
  - unit + architecture + client tests
  - integration tests (Docker is available on `ubuntu-latest`)
  - pack Contracts + Client
  - build the image (no push)
  - every action SHA-pinned; minimal `permissions`
- **`release.yml`** (on tag `v*`):
  - run the same gates
  - push Contracts + Client to GitHub Packages
  - push the image to `ghcr.io/gresta-vertex-labs/{platform-servicetemplate}` with the version tag
  - `packages: write` only on the jobs that publish
- ✅ `docker build` works locally (with the secret); the container starts and answers `/health/live`.
  The workflows pass `actionlint` if you can run it (say so if not).

### Phase 7: Templatize
- `template.json` per section 8. Put the tokens in place throughout the content (the content keeps
  building with them).
- `Platform.ServiceTemplate.csproj` packs `content/**` as a template package (`PackageType=Template`,
  `IncludeContentInPack`, no build output), excluding `bin/obj`.
- ✅ `dotnet new install ./` works; `dotnet new sk-service -n Shipping --aggregate Shipment -o <tmp>`
  generates a repo that builds with 0 warnings and passes all tests.

### Phase 8: Template verification
- `eng/verify-template.sh <case>`:
  1. install the local template
  2. generate into a temp folder outside the repo
  3. build
  4. run the unit, architecture and client tests (integration tests too when `--integration` is passed)
  5. **fail if any token survives** (`Platform.ServiceTemplate`, `platformservicetemplate`,
     `platform-servicetemplate`, `TemplateItem`, `templateItem`, `template_item`, `template-items`) in
     any file name or file content
  6. check the skill folder is present and stamped
- Cases:
  - `-n Shipping --aggregate Shipment`
  - `-n Acme.Billing --aggregate Invoice` (dotted name)
  - `-n Catalog` with the default aggregate
- `ci.yml` of this repo runs all cases (integration on one of them).
- ✅ All three cases pass locally and in CI.
- ⏸ **Checkpoint 2.** Stop and report the results, the generated tree for `Shipping`, and any decisions
  taken.

### Phase 9: Release
- This repo's `release.yml`: on tag `v*`, run the verification, pack, and push `Platform.ServiceTemplate`
  to GitHub Packages.
- The README:
  - install from the feed (`dotnet new install Platform.ServiceTemplate::<version>` with the feed
    configured)
  - create a service
  - first steps (`git init`, create the GitHub repo, set the `GITHUB_PACKAGES_TOKEN` secret, run the
    AppHost)
  - the options
  - known gaps
- `CHANGELOG.md` first entry.
- ✅ Ready to tag. **Do not tag**; ask the user.

### Phase 10: Acceptance with the AI (after the user tags and publishes)
- Install the published template, generate `Shipping`, and run these tasks headless (`claude -p`, one
  fresh process each, in the generated repo), each ending with: *"If something is ambiguous, make the
  most reasonable assumption, state it, and proceed; then make sure the solution builds and the tests
  pass."*
  1. "Add a `MarkShipmentDispatched` command and a `POST /v1/shipments/{id}/dispatch` endpoint that
     publishes `ShipmentDispatched`."
  2. "Cache `GetShipmentQuery` for five minutes per tenant and clear it when the shipment changes."
  3. "Consume Ordering's `ordering.order_placed` event and create a shipment for it, deduplicating
     redelivered messages."
  4. "Run a job every night at 02:00 on one replica that marks shipments older than 30 days as stale."
- Score each task on: right packages, right project, the build clean, the tests green, the
  architecture tests green.
- Record the results in `README.md` → "Template acceptance". A miss in **package choice** is reported to
  the kernel (skill routing), not fixed here. A miss caused by the **template** (wiring, docs) is fixed
  here.

## 11. Definition of done

- A generated service builds with 0 warnings, all four test projects pass, and the architecture rules
  catch a wrong reference.
- The AppHost runs end to end with two tenants isolated.
- The image builds and passes its health checks.
- Contracts and Client pack with package validation on.
- The skill is synced, matching `SharedKernelVersion`, and both sync scripts work.
- No token survives generation in any of the three cases.
- Both `CLAUDE.md` files are correct for their audience.
- The template package is published, and phase 10 is recorded.

## 12. Kernel version bump (procedure to document in this repo's `CLAUDE.md`)

1. Change `SharedKernelVersion` in the content's `Directory.Packages.props`.
2. Run the sync script inside the content.
3. Fix whatever the kernel's release notes require.
4. Run `eng/verify-template.sh` for all cases.
5. Write a CHANGELOG entry with the manual steps for existing services (usually: bump the version, run
   the sync).
6. Release the template.

## 13. Open questions: ask the user in phase 0

1. **Package ids of Contracts and Client:** `{Name}.Contracts` (for example `Shipping.Contracts`), or
   with a company prefix (`Gresta.Shipping.Contracts`)? The answer may add a `--package-prefix` option.
2. **License and repo visibility** of generated services and of this template (the kernel is MIT).
3. **The Keycloak realm:** reuse Shop's realm structure and claim names as-is, or does production use
   different claim names (tenant, permissions) that the dev realm should match?
4. **Container registry:** `ghcr.io/gresta-vertex-labs/<service>` is assumed. Confirm.

## 14. Out of scope

- A second "thin service" template (gateway/BFF, relay). Build it later from real examples.
- Helm or K8s manifests.
- Moving the shape rules into `SharedKernel.ArchitectureTests`, a skill recipe for "publish/consume a
  service client", and making the vulnerable-package audit a kernel CI gate. These are kernel
  follow-ups; list them in this repo's README "Known gaps".
- Retrofitting existing services.
