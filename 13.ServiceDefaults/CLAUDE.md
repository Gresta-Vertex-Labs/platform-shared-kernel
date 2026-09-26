# 13.ServiceDefaults — Host Composition Brain

## What This Domain Is

The host composition packages a microservice calls in `Program.cs`: OpenTelemetry, health endpoints and readiness,
the startup gate, rate limiting, the HTTP request context (caller + correlation id), tenant resolution, mTLS,
Key Vault configuration and request-culture resolution. Composition glue only — no business logic, no domain types.

Every package here is **Host tier** (`<SharedKernelTier>Host</SharedKernelTier>`). A Host package may reference any
Foundation, Model, Abstractions, Adapter or Host package; nothing below Host may reference one. The build enforces
this (`eng/SharedKernelTiers.targets`, SKTIER001–006 are errors), so this file states no numbered-layer rules.

Philosophy: **Composition-only. Opt-in by default. Liveness ≠ Readiness. One request context.**

---

## Packages

| Package | Provides | References |
| --- | --- | --- |
| `SharedKernel.ServiceDefaults` | **Composition base.** `AddServiceDefaults()` (OTel traces/metrics/logs + base health checks), `AddSharedKernelHealthChecks()`, `MapDefaultHealthCheckEndpoints(requireAuthorization)`, `StartupGate`/`StartupGateHealthCheck`, `AddSharedKernelReadiness()` (maps every `IReadinessProbe` to a `ready` check), `HealthCheckNames`/`HealthCheckTags`/`HealthCheckRegistrationLogging`, every `WithXTelemetry()`, `AddSharedKernelRateLimiting()`/`RateLimitPolicyNames`, `BaggageLogRecordProcessor` (copies only `correlation.id`/`TenantId`) and `RequestBaggageRefusingPropagator` (a request's `baggage` header never fills `Baggage.Current`) — P-562 X2 | `SharedKernel.Primitives` only (Foundation) + OpenTelemetry. Locked by `CompositionBaseIsolationTests` |
| `SharedKernel.ServiceDefaults.Security` | `AddSharedKernelRequestContext()` and `app.UseSharedKernelRequestContext()` — the one `IRequestContext` over `12.Security`'s `IUserContext`, and the HTTP inbound adapter that owns the correlation id and the request's `RequestContextScope`, and refuses inbound W3C baggage at the edge (`TrustInboundBaggage`) | base, `SharedKernel.Execution`, `SharedKernel.Security.Abstractions` |
| `SharedKernel.ServiceDefaults.Persistence` | `AddDatabaseReadinessCheck<TContext>()`, `AddDapperDatabaseReadinessCheck()`, `AddPersistenceStartupReadinessCheck()` | base, `Persistence.Abstractions`, `Persistence.EfCore` |
| `SharedKernel.ServiceDefaults.Security.Mtls` | `AddMtlsClientCertificate()`, `AddMtlsForwardedHeaderCertificate()`, `MtlsForwardedHeaderMiddleware`, `MtlsForwardedHeaderOptions` | base, `Security.Mtls` |
| `SharedKernel.ServiceDefaults.Configuration.KeyVault` | `AddSharedKernelKeyVaultConfiguration(vaultUri, credential?)` — Key Vault secrets as an `IConfiguration` source | base, `Azure.Extensions.AspNetCore.Configuration.Secrets`, `Azure.Identity` |
| `SharedKernel.ServiceDefaults.Localization` | `AddSharedKernelLocalization()` and its request-culture providers | base, `MultiTenancy`, `Security.Abstractions` |
| `SharedKernel.MultiTenancy` | `AddSharedKernelMultiTenancy()`, `TenantResolutionMiddleware`, the Claim/Header/Database `ITenantResolutionStrategy` set, `TenantResolutionOptions`, `ITenantStatusValidator`, the tenant catalog (`ITenantCatalog`, `TenantDescriptor`, `DatabaseTenantCatalog`, `CachedTenantCatalog`, `CatalogTenantStatusValidator`) | `Primitives`, `Execution`, `Security.Abstractions`, `Persistence.Abstractions`, `Caching.Abstractions` |

There are no per-dependency readiness packages. Every provider registers its own `IReadinessProbe`
(`SharedKernel.Primitives.Health`) when it is registered, and the base maps all of them with one call. Probe names:
`messaging`, `redis`, `cache`, `encryption-key-provider`, `field-encryption`, `audit-sealing`, `storage-{store}`,
`search-{provider}-{index}`, `vector-store-{provider}-{collection}`, `workflows`, `scheduler`. The Azure Key Vault
key provider is registered by `01.Core`'s `SharedKernel.Cryptography.KeyVault.Azure`
(`AddSharedKernelCryptography(configuration).AddAzureKeyVaultEncryption(configuration)`), not here. **Since P-562 X4**
it is also the root of `06.Persistence`'s ETags with no opt-in: `AddSharedKernelPostgres` seals every `EntityVersion`
with an HKDF subkey of the unkeyed `ISynchronousEncryptionKeyProvider`, else of the unkeyed `IEncryptionKeyProvider`.
With an asynchronous provider a hosted warm-up loads the key before the host takes traffic (up to 10 seconds;
readiness does not wait for it), and it is refreshed every 5 minutes.

All projects target `net10.0` with `ImplicitUsings` and `Nullable`; tests are nested `{Package}.Tests` projects.

---

## Canonical `Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddSharedKernelKeyVaultConfiguration(new Uri("https://my-vault.vault.azure.net/")); // optional, [.Configuration.KeyVault]
builder.AddServiceDefaults();                               // FIRST: OTel + startup gate + base health checks
builder.Services.AddOidcAuthentication(builder.Configuration); // 12.Security — registers IUserContext
builder.Services.AddSharedKernelRequestContext();           // [.Security] IRequestContext + IRequestContextAccessor
builder.Services.AddSharedKernelMultiTenancy();             // optional, multi-tenant services
builder.AddSharedKernelRateLimiting();                      // optional

builder.Services.AddHealthChecks()                          // never a second AddSharedKernelHealthChecks()
    .AddDatabaseReadinessCheck<OrdersDbContext>()           // [.Persistence]
    .AddSharedKernelReadiness();                            // every provider's IReadinessProbe

builder.WithMessagingTelemetry().WithPersistenceTelemetry(); // only the domains the service uses

var app = builder.Build();

app.UseSharedKernelRequestContext();                        // FIRST: correlation id + request context scope
app.UseExceptionHandler();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();            // optional; after UseAuthentication()
app.UseRateLimiter();                                       // when AddSharedKernelRateLimiting() is used
app.UseAuthorization();

app.MapDefaultHealthCheckEndpoints();                       // /health/live, /health/ready
app.Run();
```

With `14.Presentation`'s `builder.AddSharedKernelWebApi()`, the pipeline after `UseSharedKernelRequestContext()` is
`app.UseSharedKernelWebApi(p => p.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()));
app.MapEndpoints();` — `UseSharedKernelWebApi()` adds the exception handler, authentication, `UseRateLimiter()` and
authorization in a fixed order.

`samples/OrderApi/OrderApi.Api/Program.cs` is the compiled reference for this order.

---

## Interface Contracts

### Composition base

```text
AddServiceDefaults(this IHostApplicationBuilder) → IHostApplicationBuilder
    AddSharedKernelTelemetry(entry-assembly name) + AddSharedKernelHealthChecks(). First call in Program.cs.
    Registers no dependency-specific check. Outbound HTTP resilience is 11.Communication's concern.

AddSharedKernelTelemetry(this IHostApplicationBuilder, string serviceName) → IHostApplicationBuilder
    Tracing (ASP.NET Core, HttpClient, EF Core), metrics (ASP.NET Core, HttpClient, runtime), logs with
    IncludeScopes + IncludeFormattedMessage, BaggageLogRecordProcessor, OTLP exporter configured only by the
    standard OTEL_EXPORTER_OTLP_* environment variables.
    P-562 X2: also registers ConfigureOpenTelemetryTracerProvider((_, _) => RequestBaggageRefusingPropagator.Install())
    — OpenTelemetry's default propagator is decorated when the tracer provider is built, so an incoming request's
    baggage header never fills Baggage.Current and is never forwarded by the HttpClient/gRPC client
    instrumentations. Idempotent; a propagator set before the host starts is wrapped, one set after start removes the
    protection.

BaggageLogRecordProcessor  (internal, Telemetry/)
    For each key of PlatformBaggageKeys.All — "correlation.id" and "TenantId", nothing else (P-562 X2) — reads
    Activity.Current?.GetBaggageItem(key) (walks the parent chain) when the record is finalized and appends it under
    the same key, unless the value contains a control character or U+2028/U+2029, or the record already has that
    attribute. No-op when Activity.Current is null or carries neither key.
    MESSAGE PATH: MassTransit copies the publisher's baggage (MT-Activity-Correlation-Context) onto the consuming
    activity; the allow-list keeps only the two keys on consumer logs. A consumer's tenant and actor come from
    dedicated headers (07.Messaging's WithInboundRequestContext()), never from baggage.

AddSharedKernelHealthChecks(this IServiceCollection) → IHealthChecksBuilder
    StartupGate singleton + StartupGateHealthCheck ("startup", tagged ready). Called by AddServiceDefaults();
    a second call duplicates "startup" and the host throws at startup.

AddSharedKernelReadiness(this IHealthChecksBuilder, Action<ReadinessHealthCheckOptions>? configure = null)
    One health check per registered IReadinessProbe, named after the probe, tagged "ready".
    Degraded → Degraded, Unhealthy → Unhealthy; a throwing probe → Unhealthy with the exception type only
    (logged as EventId 13005). Latency goes into the check data as LatencyMilliseconds. Probes are read when
    health checks are first resolved, so registration order does not matter. Duplicate names throw.
    ReadinessHealthCheckOptions: Timeout (per check), Exclude(probeName).

MapDefaultHealthCheckEndpoints(this IEndpointRouteBuilder, bool requireAuthorization = false)
    /health/live → "live"-tagged checks only; /health/ready → "ready"-tagged checks only.
    requireAuthorization chains RequireAuthorization() on both — defense in depth only.

StartupGate.MarkReady()   — /health/ready stays Unhealthy until called; idempotent.
HealthCheckRegistrationLogging.LogRegistration(...) — public so a service's own check logs EventId 13002.
```

`WithXTelemetry()` wires a domain's instruments **by name** and references nothing:

| Method | Traces | Metrics |
| --- | --- | --- |
| `WithApplicationTelemetry()` | `SharedKernel.Application` | `SharedKernel.Application` + a seconds-based bucket view for `sharedkernel.application.request.duration` |
| `WithCachingTelemetry()` | `SharedKernel.Caching` | `SharedKernel.Caching` |
| `WithCommunicationTelemetry()` | gRPC client instrumentation | `Polly` (Polly v8 has no `ActivitySource`) |
| `WithIntegrationTelemetry()` | `SharedKernel.Integration` | — |
| `WithIntelligenceTelemetry()` | `SharedKernel.AI` | `SharedKernel.AI` |
| `WithMessagingTelemetry()` | `MassTransit`, `SharedKernel.Messaging` | `MassTransit` |
| `WithPersistenceTelemetry()` | `SharedKernel.Persistence`, `SharedKernel.Persistence.EfCore.Auditing`, `Npgsql` | those three + `SharedKernel.Persistence.EfCore.Encryption` |
| `WithSchedulingTelemetry()` | `SharedKernel.Scheduling` | `SharedKernel.Scheduling` |
| `WithSearchTelemetry()` | `SharedKernel.Search` | `SharedKernel.Search` |
| `WithStorageTelemetry()` | `SharedKernel.Storage` | `SharedKernel.Storage` |
| `WithWorkflowTelemetry()` | `SharedKernel.Workflows` | `SharedKernel.Workflows` |

All are idempotent: a second call registers no duplicate instrument or view.

```text
AddSharedKernelRateLimiting(this IHostApplicationBuilder, Action<RateLimiterOptions>? configure = null)
    BCL Microsoft.AspNetCore.RateLimiting: a global fixed window per remote IP (100/min) plus the named
    RateLimitPolicyNames.Authentication policy (10/min). configure runs last. OnRejected deliberately left unset
    (P-562): with 14.Presentation's AddSharedKernelWebApi() the platform's 429 application/problem+json body
    (errorCode rate_limit.exceeded, Retry-After from the limiter) is automatic and UseSharedKernelWebApi() adds
    UseRateLimiter() itself; without it, the BCL bare 429 and the service calls app.UseRateLimiter(). A service's
    own OnRejected set in configure wins. Never a 14.Presentation reference from this domain.
```

### `SharedKernel.ServiceDefaults.Security`

```text
AddSharedKernelRequestContext(this IServiceCollection) → IServiceCollection
    TryAdd IRequestContextAccessor → RequestContextAccessor; TryAdd scoped SecurityRequestContext (internal);
    Add transient IRequestContext = RequestContextScope.Current ?? scoped SecurityRequestContext.
    Add (not TryAdd) so it replaces Persistence.EfCore's fail-closed AnonymousRequestContext in any order.
    Requires an IUserContext registration (AddOidcAuthentication, AddApiKeyAuthentication, …).

UseSharedKernelRequestContext(this IApplicationBuilder) → IApplicationBuilder
    Throws InvalidOperationException when AddSharedKernelRequestContext() was not called.
    RequestContextMiddleware: keeps the caller's X-Correlation-Id when CorrelationIds.IsValid accepts it
    (≤128 chars, [A-Za-z0-9-_:.]), else creates CorrelationIds.New(); a rejected value is never logged
    (only its length, EventId 13007; a created id logs EventId 13006 at Debug). Sets Activity baggage
    WellKnownBaggageKeys.CorrelationId, echoes the header via Response.OnStarting (also on error responses),
    and runs the request inside RequestContextScope.Begin(HttpRequestContext).
    Refuses inbound W3C baggage at the edge: the caller's items are cleared from the request Activity unless
    TrustInboundBaggage is set (off by default; moved here from 14.Presentation's WebApi by P-579, P-562 X2).
    HttpRequestContext reads the caller lazily from SecurityRequestContext, because the middleware runs before
    UseAuthentication() and the scoped IUserContext snapshots HttpContext.User when first created.
```

`SecurityRequestContext` mapping: `UserId` = `SubjectId` ?? `ClientId` (null when unauthenticated);
`TenantId` = `IUserContext.TenantId` (`TenantId?`, null fails closed); `ActorKind` = `IUserContext.ActorKind` when
authenticated, `ActorKind.Anonymous` otherwise (never `System`); `ClientId`, `SessionId`; `HasPermissionAsync` =
`IUserContext.HasPermission` (ordinal). `CorrelationId` comes from the HTTP scope, not from this type.

### `SharedKernel.ServiceDefaults.Persistence`

```text
AddDatabaseReadinessCheck<TContext>(this IHealthChecksBuilder, string name = "database") where TContext : SharedKernelDbContext
    Unhealthy until IPersistenceStartup completed, then TContext's CheckReadinessAsync. Tags ready, db.
AddDapperDatabaseReadinessCheck(this IHealthChecksBuilder, string name = "database-dapper")
    IDbConnectionFactory readiness. Tags ready, db.
AddPersistenceStartupReadinessCheck(this IHealthChecksBuilder, string name = "persistence-startup")
    Unhealthy until startup migrations and seeders finished. Tags ready, db.
```

Field encryption and audit sealing are **not** checks here: `UseFieldEncryption()` and `UseAuditTrail()` register
the `field-encryption` and `audit-sealing` probes, which `AddSharedKernelReadiness()` maps. The audit lag limit is
`AuditSealerOptions.MaxReadyLag`.

### `SharedKernel.MultiTenancy`

```text
AddSharedKernelMultiTenancy(this IServiceCollection, Action<TenantResolutionOptions>? configure = null)
    TenantResolutionOptions (ValidateOnStart via TenantResolutionOptionsValidator), TryAdd IRequestContextAccessor,
    scoped Header/Claim/Database strategies. Does NOT add the middleware.

ITenantResolutionStrategy { string StrategyName; Task<TenantId?> TryResolveAsync(HttpContext, CancellationToken) }
    Claim    — UserContextResolver.Resolve(context.User, IUserContextMapper set).TenantId; never parses claims itself.
    Header   — WellKnownHeaders.TenantId ("X-Tenant-Id"), TenantId.TryParse; malformed → null, never throws.
    Database — parameterized lookup through IDbConnectionFactory, async, CancellationToken threaded through.

TenantResolutionOptions.StrategyOrder (section SharedKernel:MultiTenancy) — empty means DefaultStrategyOrder
    [Claim, Header, Database]; a configured order replaces the default.

TenantResolutionMiddleware — after UseAuthentication(). Runs strategies in order; first non-null wins; an optional
    ITenantStatusValidator (IsActiveAsync(TenantId, ct)) returning false fails closed exactly like "not resolved".
    Sets WellKnownBaggageKeys.TenantId baggage when resolved, then opens an INNER RequestContextScope over
    RequestContextScope.Current ?? registered IRequestContext ?? AnonymousRequestContext with WithTenant(resolved):
    only the tenant changes; caller and correlation id stay the outer scope's. Unresolved → TenantId null.
    Logs TenantResolved (13100) / TenantNotResolved (13101).

ITenantCatalog { GetByIdAsync(TenantId, ct); GetByResolutionKeyAsync(string, ct) } — read-only lookup.
    DatabaseTenantCatalog(IDbConnectionFactory); CachedTenantCatalog(inner, ICacheService, ICacheKeyProvider, ttl?)
    (30 s default, fail-safe and eager refresh off, InvalidateTenantAsync(TenantId, ct));
    CatalogTenantStatusValidator(ITenantCatalog) — a catalog miss is inactive.
```

### mTLS, Key Vault configuration, localization

- `AddMtlsClientCertificate(mode)` — Kestrel `ConfigureHttpsDefaults`; acceptance delegated to `12.Security`'s
  `IMtlsCertificateValidator`, resolved per handshake from a fresh scope. Kestrel's validation callback is
  synchronous, so the async validator is bridged with a blocking wait: the validator must answer from memory.
- `AddMtlsForwardedHeaderCertificate(configure)` + `app.UseMiddleware<MtlsForwardedHeaderMiddleware>()` — for TLS
  terminated at an ingress. `HeaderName` is required (no vendor default). `TrustedNetworks`
  (`AddTrustedNetwork`/`AddTrustedProxy`) restricts which remote addresses may set the header; unset logs a one-time
  warning (13003). Certificate bytes are never logged (13000/13001 log thumbprint/subject only).
- `AddSharedKernelKeyVaultConfiguration(vaultUri, credential?)` — `DefaultAzureCredential` by default; an unreachable
  vault throws from the call at startup (`ConfigurationManager` loads eagerly).
- `AddSharedKernelLocalization(configure)` — `StrategyOrder` default `[UserPreference, TenantDefault,
  AcceptLanguageHeader]`: signed signals before the unsigned header. `TenantDefault` reads the request context's
  tenant and an optional `ITenantCatalog`. The service still calls `app.UseRequestLocalization()`. Neither dynamic
  step configured → one-time warning (13004).

---

## Implementation Rules

- **The base references `SharedKernel.Primitives` and OpenTelemetry only.** Every service restores what the base
  references. `CompositionBaseIsolationTests` locks it twice: by assembly references (used types) and by project file
  (unused or `const`-only references leave no trace in IL). Anything needing another kernel package goes in a
  `SharedKernel.ServiceDefaults.*` package or, for readiness, behind an `IReadinessProbe` the provider registers.
- **Never add a per-dependency readiness package or `Add*ReadinessCheck` for a provider.** The provider owns its probe;
  `AddSharedKernelReadiness()` maps it. A probe's constructor must be cheap; it resolves clients inside `ProbeAsync`.
- **An integration package references the base plus what its own integration needs, never another integration
  package.** XML docs name a sibling integration's types as `<c>Name</c>`, never `<see cref>`.
- **New package names:** `SharedKernel.ServiceDefaults.{Capability}[.{Provider}]`, then check the path length:
  `…\{Name}\{Name}.Tests\obj\Release\net10.0\{Name}.Tests.dll` must stay ≤ 245 characters at
  `C:\Github\platform-shared-kernel`.
- **Readiness checks chain onto `builder.Services.AddHealthChecks()`**, never a second `AddSharedKernelHealthChecks()`.
- **Live ≠ ready.** Anything that depends on an external system is tagged `ready`, never `live`. `/health/live`
  answers only "is the process alive". A backlog, queue depth or registered-job count is data, never a failure.
- **Shared helpers an integration needs from the base are public API**, never `InternalsVisibleTo`.
- **One request context.** `UseSharedKernelRequestContext()` is the only HTTP inbound adapter that creates the
  request's correlation id and scope; it runs first, before `UseExceptionHandler()`. Tenant resolution only replaces
  the tenant in an inner scope. Never add a second correlation-id middleware, and never read `Activity.Id`/`TraceId`
  as the correlation id.
- **Tenant strategy order is a security default.** `[Claim, Header, Database]` puts the signature-verified claim before
  the forgeable header. Never reorder it without a security review. `StrategyOrder` is empty by default because
  configuration binding appends to a non-empty list.
- **Fail closed on tenants.** No resolved tenant, an inactive tenant and a catalog miss all mean `TenantId` = `null`.
  `ITenantStatusValidator` is optional (`GetService`); `null` means "not registered".
- **`DatabaseTenantResolutionStrategy`/`DatabaseTenantCatalog` use parameterized SQL only.**
- **`ITenantCatalog` is read-only.** Provisioning/onboarding never goes on it. `CachedTenantCatalog`'s TTL stays short
  and bounded; call `InvalidateTenantAsync` after a status change.
- **Propagation identifiers come from `01.Core`** (`WellKnownHeaders`, `WellKnownBaggageKeys`) — never a local literal.
- **`BaggageLogRecordProcessor` copies only the platform's own baggage keys** — `correlation.id` and `TenantId`
  (`PlatformBaggageKeys`, retyped from `WellKnownBaggageKeys` and pinned to it by a test) — and never a value containing
  a control character or U+2028/U+2029 (P-562 X2, owner-approved; supersedes WO-041's "generic, never names a key").
  Baggage also arrives from callers (the W3C `baggage` header) and from message headers (MassTransit copies
  `MT-Activity-Correlation-Context` onto the consuming activity); copying every item let an anonymous caller put any
  property on every log record of its request. A key belongs on the list only if platform middleware writes and
  *replaces* it. An explicit attribute on the record wins over baggage.
- **OpenTelemetry's `Baggage.Current` is never filled from an incoming request** (P-562 X2):
  `AddSharedKernelTelemetry` decorates `Propagators.DefaultTextMapPropagator` with `RequestBaggageRefusingPropagator`
  when the tracer provider is built. Extraction from an `HttpRequest` carrier keeps the trace context and drops the
  baggage; every other carrier and all injection are unchanged. Never replace or remove the decorator, and never read
  a caller's identity from baggage.
- **Never log** certificate bytes, raw tokens, raw header values or a rejected correlation id — lengths, thumbprints
  and subjects only.
- **`AddSharedKernelRateLimiting()` is never called by `AddServiceDefaults()`** and never references
  `14.Presentation`. It never sets a default `OnRejected` (P-562): `14.Presentation`'s `AddSharedKernelWebApi()`
  fills `OnRejected` only when nothing else has, so a default here would take the platform's 429 body away from every
  service composing both packages. The docs never show a hand-rolled rejection body.
- **`AddSharedKernelKeyVaultConfiguration()` stays distinct from the Key Vault key provider**
  (`AddAzureKeyVaultEncryption` in `SharedKernel.Cryptography.KeyVault.Azure`): secrets as configuration versus keys
  for encryption.
- **Health endpoints must be network-restricted** (ingress / `NetworkPolicy`) wherever they are not intentionally
  public; `requireAuthorization: true` is defense in depth only and breaks unauthenticated kubelet probes.
- **EventIds.** The base and every `SharedKernel.ServiceDefaults.*` package share `13000`–`13099`, allocated one id at
  a time and never reused: 13000/13001/13003 `.Security.Mtls`, 13002/13005 base, 13004 `.Localization`,
  13006/13007 `.Security`. `SharedKernel.MultiTenancy` owns `13100`–`13199` (13100, 13101).
- **Only `StartupGate` holds mutable static-like state** (a `volatile bool` singleton). No reflection-based dispatch
  anywhere in the domain.

---

## AOT

OpenTelemetry, the BCL health-check/rate-limiting APIs, `StartupGate`, the strategies and the middleware are
reflection-free. Configuration-bound options and the Azure Key Vault configuration provider are not trim-clean and
are not claimed to be. HotChocolate/ASP.NET Core concerns belong to `14.Presentation`.

---

## Test Rules

- Tests live in each package's nested `{Package}.Tests` project; `SharedKernel.ServiceDefaults.Persistence.Tests`
  needs Docker.
- Every readiness check is asserted tagged `ready` and never `live`, and its default name is the `HealthCheckNames`
  constant.
- `AddSharedKernelReadiness()`: each registered probe becomes one check with the probe's name; status mapping,
  exclusion, timeout, duplicate-name failure and a throwing probe are covered with test-double probes.
- `WithXTelemetry()`: a span or measurement from the named source is captured, and a second call registers no
  duplicate.
- Endpoint tests use `HostBuilder().ConfigureWebHost(w => w.UseTestServer())` + `GetTestClient()`.
- Request context: `SharedKernel.ServiceDefaults.Security.Tests/Propagation/EndToEndPropagationTests` proves correlation
  id, tenant and caller survive HTTP → REST, HTTP → gRPC, HTTP → bus → consumer → REST and job → REST in-process.
- Tenant strategies: header present/absent/malformed; claim resolution through a registered `IUserContextMapper`;
  database resolution parameterized and genuinely async; strategy order default and configured replacement;
  inactive tenant fails closed; the middleware's inner scope replaces only the tenant.
- mTLS: validator resolved per handshake; forwarded header ignored outside `TrustedNetworks`; the unconfigured-trust
  warning fires exactly once.
- `RateLimitRejectionRecipeTests` (rewritten by P-562) drives real hosts with a test-only reference to
  `SharedKernel.Presentation.WebApi`: with `AddSharedKernelWebApi()` + `AddSharedKernelRateLimiting()` +
  `UseSharedKernelWebApi()` and nothing written by the service, a policy and a global-limiter rejection are 429
  `application/problem+json`, `rate_limit.exceeded`, `Retry-After`; a service's own `OnRejected` wins; without
  `14.Presentation` the answer is a bare 429. The production base never references it.
- `BaggageLogRecordProcessor` (P-562 X2): the two platform keys on `Activity.Current` or a parent are copied; every
  other key (`SubjectId`, `tenant.id`, case variants, MassTransit's `messaging.*` keys) is not; a value with a C0/DEL/C1
  control character or U+2028/U+2029 is not copied; the keys equal `WellKnownBaggageKeys`.
- `RequestBaggageRefusingPropagator` (P-562 X2, a non-parallel collection because the tests replace the process-wide
  default propagator and restore it): extraction from an `HttpRequest` keeps the trace id and drops the baggage, the
  undecorated SDK default reads it (non-vacuity), a dictionary carrier keeps its baggage, injection is unchanged.
- `CompositionBaseIsolationTests` must fail when a SharedKernel reference is added to the base project.

History of this domain lives in [`state-map.md`](state-map.md) and the root [`CLAUDE.changelog.md`](../CLAUDE.changelog.md).
