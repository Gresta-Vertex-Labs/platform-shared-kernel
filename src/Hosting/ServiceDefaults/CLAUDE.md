# 13.ServiceDefaults — Domain Brain

> The host composition packages a microservice calls in `Program.cs`: OpenTelemetry, health endpoints and readiness,
> the startup gate, rate limiting, the HTTP request context (caller + correlation id), tenant resolution and the
> tenant catalog, Kestrel mTLS, Key Vault as a configuration source, and request-culture resolution. Composition glue
> only — no business logic, no domain types. It deliberately does **not** own per-dependency readiness checks (each
> provider registers its own `IReadinessProbe`), the 429 problem body (`14.Presentation`), outbound HTTP resilience
> (`11.Communication`), or Key Vault as an encryption-key source (`01.Core`'s `SharedKernel.Cryptography.KeyVault.Azure`).
> Philosophy: composition-only, opt-in by default, liveness ≠ readiness, one request context.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.ServiceDefaults` | Host | **Composition base.** OTel, health checks, `StartupGate`, `AddSharedKernelReadiness()`, every `WithXTelemetry()`, rate limiting, `BaggageLogRecordProcessor`, `RequestBaggageRefusingPropagator`. References `SharedKernel.Primitives` + OpenTelemetry only |
| `SharedKernel.ServiceDefaults.Security` | Host | `AddSharedKernelRequestContext()` / `UseSharedKernelRequestContext()` — the one `IRequestContext` over `IUserContext`; correlation id; refusal of inbound baggage |
| `SharedKernel.ServiceDefaults.Persistence` | Host | Database and persistence-startup readiness checks |
| `SharedKernel.ServiceDefaults.Security.Mtls` | Host | Kestrel client-certificate negotiation; forwarded-header certificate for TLS terminated at an ingress |
| `SharedKernel.ServiceDefaults.Configuration.KeyVault` | Host | Azure Key Vault secrets as an `IConfiguration` source |
| `SharedKernel.ServiceDefaults.Localization` | Host | `AddSharedKernelLocalization()` and its request-culture providers |
| `SharedKernel.MultiTenancy` | Host | `TenantResolutionMiddleware`, Claim/Header/Database strategies, `ITenantStatusValidator`, the read-only tenant catalog |

`consumer-verify/` (not packable, Unit lane) compiles a consumer against the base and `MultiTenancy` packages.
Empty folders such as `SharedKernel.ServiceDefaults.Messaging`, `.Caching`, `.AI` hold only stale `bin`/`obj` output — no project.

## Public Entry Points

**Composition base (`SharedKernel.ServiceDefaults`)**
- `builder.AddServiceDefaults()` — first call: `AddSharedKernelTelemetry(entry-assembly name)` + `AddSharedKernelHealthChecks()`. Registers no dependency-specific check.
- `builder.AddSharedKernelTelemetry(serviceName)` — tracing (ASP.NET Core, HttpClient, EF Core), metrics (ASP.NET Core, HttpClient, runtime), logs with scopes, `BaggageLogRecordProcessor`, OTLP exporter configured only by the standard `OTEL_EXPORTER_OTLP_*` environment variables; installs `RequestBaggageRefusingPropagator` when the tracer provider is built.
- `services.AddSharedKernelHealthChecks()` — `StartupGate` + `StartupGateHealthCheck` (`startup`, tag `ready`). Called by `AddServiceDefaults()`; a second call duplicates `startup` and the host throws.
- `healthChecks.AddSharedKernelReadiness(o => …)` — one `ready` check per registered `IReadinessProbe`, named after the probe; `ReadinessHealthCheckOptions` (`Timeout`, `Exclude(name)`); a throwing probe → Unhealthy with the exception type only (13005); duplicate names throw.
- `app.MapDefaultHealthCheckEndpoints(requireAuthorization: false)` — `/health/live` (`live` tag) and `/health/ready` (`ready` tag).
- `StartupGate.MarkReady()`; `HealthCheckNames`, `HealthCheckTags`, `HealthCheckRegistrationLogging.LogRegistration` (public so a service's own check logs 13002).
- `builder.With{Application,Caching,Communication,Integration,Intelligence,Messaging,Persistence,Reporting,Scheduling,Search,Storage,Workflow}Telemetry()` — subscribe to each domain's `ActivitySource`/`Meter` **by name**; reference nothing; idempotent. `WithCommunicationTelemetry` adds gRPC client instrumentation and the `Polly` meter; `WithApplicationTelemetry` adds a seconds-based bucket view.
- `builder.AddSharedKernelRateLimiting(o => …)` — BCL rate limiter: global fixed window per remote IP (100/min) + `RateLimitPolicyNames.Authentication` (10/min); `configure` runs last.

**`SharedKernel.ServiceDefaults.Security`**
- `services.AddSharedKernelRequestContext(o => …)` (`RequestContextOptions.TrustInboundBaggage`, default `false`) — `IRequestContextAccessor`, scoped `SecurityRequestContext`, transient `IRequestContext = RequestContextScope.Current ?? SecurityRequestContext`. Needs an `IUserContext` registration from `12.Security`.
- `app.UseSharedKernelRequestContext()` — the first middleware; throws if the services were not added.

**`SharedKernel.ServiceDefaults.Persistence`** — `AddDatabaseReadinessCheck<TContext>(name = "database")`, `AddDapperDatabaseReadinessCheck(name = "database-dapper")`, `AddPersistenceStartupReadinessCheck(name = "persistence-startup")`; all tagged `ready`, `db`.

**`SharedKernel.MultiTenancy`** — `services.AddSharedKernelMultiTenancy(o => …)` (`TenantResolutionOptions`, section `SharedKernel:MultiTenancy`, `StrategyOrder`) then `app.UseMiddleware<TenantResolutionMiddleware>()` after authentication. Catalog: `ITenantCatalog` (`GetByIdAsync`, `GetByResolutionKeyAsync`), `TenantDescriptor`, `DatabaseTenantCatalog`, `CachedTenantCatalog` (`InvalidateTenantAsync`), `CatalogTenantStatusValidator`.

**`SharedKernel.ServiceDefaults.Security.Mtls`** — `builder.AddMtlsClientCertificate(ClientCertificateMode)`; `builder.AddMtlsForwardedHeaderCertificate(o => …)` (`MtlsForwardedHeaderOptions`, section `SharedKernel:ServiceDefaults:MtlsForwardedHeader`, `HeaderName` required, `AddTrustedNetwork`/`AddTrustedProxy`) + `app.UseMiddleware<MtlsForwardedHeaderMiddleware>()`.

**`SharedKernel.ServiceDefaults.Configuration.KeyVault`** — `builder.AddSharedKernelKeyVaultConfiguration(vaultUri, credential?)` (`DefaultAzureCredential` by default; an unreachable vault throws at startup).

**`SharedKernel.ServiceDefaults.Localization`** — `builder.AddSharedKernelLocalization(o => …)` (`LocalizationResolutionOptions`: `StrategyOrder` default `UserPreference → TenantDefault → AcceptLanguageHeader`, `UserPreferenceClaimType`); the service still calls `app.UseRequestLocalization()`.

**Canonical order** (compiled reference: `samples/OrderApi/OrderApi.Api/Program.cs`): `AddServiceDefaults()` → authentication (`12.Security`) → `AddSharedKernelRequestContext()` → optional `AddSharedKernelMultiTenancy()` / `AddSharedKernelRateLimiting()` → `AddHealthChecks().AddDatabaseReadinessCheck<T>().AddSharedKernelReadiness()` → the `WithXTelemetry()` the service needs. Pipeline: `UseSharedKernelRequestContext()` first → `UseSharedKernelWebApi(p => p.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()))` (or by hand: `UseExceptionHandler`, `UseAuthentication`, tenant middleware, `UseRateLimiter`, `UseAuthorization`) → `MapDefaultHealthCheckEndpoints()`.

## Rules & Invariants

1. **The base references `SharedKernel.Primitives` and OpenTelemetry only.** `CompositionBaseIsolationTests` locks it by assembly references and by project file. Anything needing another kernel package goes in a `SharedKernel.ServiceDefaults.{Capability}` package or behind a provider-registered `IReadinessProbe`.
2. **Never add a per-dependency readiness package or `Add*ReadinessCheck` for a provider.** The provider owns its probe; `AddSharedKernelReadiness()` maps it. A probe constructor is cheap and resolves clients inside `ProbeAsync`.
3. An integration package references the base plus what its own integration needs, **never another integration package**; XML docs name a sibling's types as `<c>Name</c>`, never `<see cref>`.
4. Readiness checks chain onto `services.AddHealthChecks()` — never a second `AddSharedKernelHealthChecks()`.
5. **Live ≠ ready.** Anything depending on an external system is tagged `ready`, never `live`. A backlog, queue depth or job count is data, never a failure.
6. Helpers an integration package needs from the base are public API, never `InternalsVisibleTo`.
7. **One request context.** `UseSharedKernelRequestContext()` is the only HTTP adapter that creates the correlation id and the request's `RequestContextScope`; it runs before `UseExceptionHandler()`. Never add a second correlation-id middleware; never read `Activity.Id`/`TraceId` as the correlation id.
8. The correlation id is kept only when `CorrelationIds.IsValid` accepts it, else `CorrelationIds.New()`; a rejected value is never logged (length only, 13007). It is echoed via `Response.OnStarting`, also on error responses.
9. `HttpRequestContext` reads the caller **lazily** from `SecurityRequestContext`, because the middleware runs before `UseAuthentication()`.
10. `AddSharedKernelRequestContext()` uses `Add` (not `TryAdd`) for `IRequestContext` so it replaces `Persistence.EfCore`'s fail-closed `AnonymousRequestContext` in any order. Mapping: `UserId = SubjectId ?? ClientId`; `TenantId` = `IUserContext.TenantId`; `ActorKind` from `IUserContext` when authenticated, else `Anonymous` (never `System`); `HasPermissionAsync` ordinal.
11. **Tenant resolution only replaces the tenant**, in an inner `RequestContextScope` (`WithTenant`); caller and correlation id stay the outer scope's. The middleware also sets `WellKnownBaggageKeys.TenantId` baggage.
12. **Tenant strategy order is a security default**: `[Claim, Header, Database]` puts the signed claim before the forgeable header. Never reorder without a security review. `StrategyOrder` defaults to empty (binding appends to a non-empty list); empty means `DefaultStrategyOrder`.
13. The claim strategy resolves through `UserContextResolver.Resolve` and the registered mappers — never parse claims here. The header strategy uses `WellKnownHeaders.TenantId` and `TenantId.TryParse`; malformed → `null`, never throws.
14. **Fail closed on tenants.** Unresolved, inactive (`ITenantStatusValidator.IsActiveAsync` false) and catalog miss all mean `TenantId = null`. `ITenantStatusValidator` is optional.
15. `DatabaseTenantResolutionStrategy` and `DatabaseTenantCatalog` use parameterized SQL only, async with the `CancellationToken` threaded through.
16. `ITenantCatalog` is read-only — never add provisioning. `CachedTenantCatalog`'s TTL (30 s default) stays short; call `InvalidateTenantAsync` after a status change.
17. **`BaggageLogRecordProcessor` copies only the platform's own keys** (`PlatformBaggageKeys`, pinned by a test to `WellKnownBaggageKeys`: correlation id and tenant) and never a value with a control character or U+2028/U+2029; an explicit record attribute wins. A key joins the list only if platform middleware writes *and replaces* it.
18. **`Baggage.Current` is never filled from an incoming request**: `RequestBaggageRefusingPropagator` decorates the default propagator; HTTP-request extraction keeps trace context and drops baggage. Never remove it; never read a caller's identity from baggage. `TrustInboundBaggage` governs only the request `Activity`'s baggage.
19. **`AddSharedKernelRateLimiting()` never sets `OnRejected`**, is never called by `AddServiceDefaults()`, and never references `14.Presentation`; `AddSharedKernelWebApi()` fills `OnRejected` with the platform 429 only when nothing else has.
20. Keep `AddSharedKernelKeyVaultConfiguration()` (secrets as configuration) distinct from `AddAzureKeyVaultEncryption` (keys for encryption).
21. `requireAuthorization: true` on health endpoints is defense in depth only (it breaks kubelet probes); endpoints must be network-restricted.
22. mTLS: the validator is resolved per handshake from a fresh scope; `MtlsForwardedHeaderOptions.HeaderName` has no vendor default; without `TrustedNetworks` a one-time warning (13003) fires.
23. **Never log** certificate bytes, raw tokens, raw header values or a rejected correlation id — lengths, thumbprints and subjects only.
24. Propagation identifiers come from `01.Core` (`WellKnownHeaders`, `WellKnownBaggageKeys`), never a local literal.
25. New packages: `SharedKernel.ServiceDefaults.{Capability}[.{Provider}]`, within the path-length budget (see MAX_PATH in the root CLAUDE.md).
26. AOT: OTel, health checks, rate limiting, `StartupGate`, strategies and middleware are reflection-free; configuration-bound options and the Key Vault configuration provider are not claimed trim-clean. `StartupGate` is the only mutable singleton state.

## Decisions

| Decision | Why |
| --- | --- |
| Providers register `IReadinessProbe`; one `AddSharedKernelReadiness()` maps them | No per-dependency packages; the host cannot forget a dependency it configured |
| Base references Foundation only | Every service restores the base; heavy dependencies stay opt-in |
| `WithXTelemetry()` subscribe by source name | No reference from the base to any capability package |
| Baggage allow-list on log records (correlation id, tenant only) | Copying every baggage item let an anonymous caller stamp any property on every log record of its request |
| Inbound baggage refused by default at two stores (propagator + request `Activity`) | Baggage flows to every outbound call; a caller could plant a tenant or user id downstream services trust |
| No default `OnRejected` in rate limiting | A default here would override `14.Presentation`'s platform 429 problem body |
| Claim → Header → Database default order | A signature-verified claim outranks an unsigned header |
| Tenant catalog is read-only | Provisioning/onboarding is a service concern, not host composition |

## Logging

Block **13000–13999**. The base and every `SharedKernel.ServiceDefaults.*` package share **13000–13099**, allocated one id at a time and never reused; `SharedKernel.MultiTenancy` owns **13100–13199**.

| EventId | Package | Event |
| --- | --- | --- |
| 13000, 13001 | `.Security.Mtls` | certificate accepted / rejected (thumbprint, subject only) |
| 13002 | base | health check registered (`HealthCheckRegistrationLogging`) |
| 13003 | `.Security.Mtls` | forwarded-header trust not configured (once) |
| 13004 | `.Localization` | no dynamic culture step configured (once) |
| 13005 | base | readiness probe threw |
| 13006, 13007 | `.Security` | correlation id created (Debug) / inbound correlation id rejected (length only) |
| 13100, 13101 | `MultiTenancy` | tenant resolved / not resolved |

Next free: 13008 (shared block), 13102 (MultiTenancy).

## Cross-Domain Couplings

- **01.Core** — `SharedKernel.Primitives` (`IReadinessProbe`, `WellKnownHeaders`/`WellKnownBaggageKeys`), `SharedKernel.Execution` (`IRequestContext`, `RequestContextScope`, `CorrelationIds`, `TenantId`). Key Vault key provider lives in `SharedKernel.Cryptography.KeyVault.Azure`.
- **12.Security** — `IUserContext`/`IUserContextMapper`/`UserContextResolver` feed the request context, the claim strategy and localization's user-preference step; `.Security.Mtls` delegates acceptance to `IMtlsCertificateValidator` and forwards certificates Oidc's RFC 8705 check reads.
- **06.Persistence** — `.Persistence` reads `IPersistenceStartup` and `SharedKernelDbContext.CheckReadinessAsync`; `MultiTenancy` uses `IDbConnectionFactory`. `field-encryption`/`audit-sealing` are probes registered by persistence, mapped by `AddSharedKernelReadiness()`.
- **02.Caching** — `CachedTenantCatalog` over `ICacheService` (Abstractions only).
- **14.Presentation** — `UseSharedKernelWebApi()` composes the pipeline after `UseSharedKernelRequestContext()` and shapes the limiter's 429; this domain never references it.
- **07.Messaging** — MassTransit copies publisher baggage onto consume activities; the log allow-list keeps only the platform keys. A consumer's tenant/actor come from `WithInboundRequestContext()`, never baggage.
- **All providers** — own their probe names: `redis`, `cache`, `messaging`, `encryption-key-provider`, `field-encryption`, `audit-sealing`, `storage-{store}`, `search-{provider}-{index}`, `vector-store-{provider}-{collection}`, `workflows`, `scheduler`, `gotenberg`.

## Testing

- Nested `{Package}.Tests` projects in the **Unit** lane, except `SharedKernel.ServiceDefaults.Persistence.Tests` (Testcontainers PostgreSQL), which is in the **Integration** lane; `consumer-verify` is in the Unit lane.
- Every readiness check is asserted tagged `ready` (never `live`) with its `HealthCheckNames` default. `AddSharedKernelReadiness()` is covered with test-double probes (mapping, exclusion, timeout, duplicate name, throwing probe).
- `WithXTelemetry()`: a span or measurement from the named source is captured; a second call adds no duplicate.
- Endpoint tests use `HostBuilder().ConfigureWebHost(w => w.UseTestServer())` + `GetTestClient()`.
- `SharedKernel.ServiceDefaults.Security.Tests/Propagation/EndToEndPropagationTests` proves correlation id, tenant and caller survive HTTP → REST, HTTP → gRPC, HTTP → bus → consumer → REST and job → REST in-process.
- `RateLimitRejectionRecipeTests` drive real hosts with a test-only reference to `SharedKernel.Presentation.WebApi` (platform 429 with it, bare 429 without, a service's `OnRejected` wins).
- `RequestBaggageRefusingPropagator` tests replace the process-wide propagator, so they run in a non-parallel collection.
- `CompositionBaseIsolationTests` must fail when a SharedKernel reference is added to the base project.
- Consumer fakes (`src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Testing`): `FakeTenantResolutionStrategy`, `InMemoryTenantCatalog`.

## Known Limitations

- Kestrel's client-certificate callback is synchronous, so `AddMtlsClientCertificate` bridges the async `IMtlsCertificateValidator` with a blocking wait — the validator must answer from memory.
- A default propagator set *after* the host starts removes the baggage-refusal protection (one set before is wrapped).
- With an asynchronous `IEncryptionKeyProvider`, persistence's ETag key is warmed by a hosted service (up to 10 s) that readiness does not wait for.
- Configuration-bound options and the Key Vault configuration provider are not trim/AOT-clean.
