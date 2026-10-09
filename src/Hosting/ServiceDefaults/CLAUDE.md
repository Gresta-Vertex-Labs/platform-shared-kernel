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

## Public Entry Points

Overloads, option keys and defaults: each package's `README.md` (`SharedKernel.ServiceDefaults*/README.md`,
`SharedKernel.MultiTenancy/README.md`).

**Composition base (`SharedKernel.ServiceDefaults`)**
- `builder.AddServiceDefaults()` — first call: `AddSharedKernelTelemetry(entry-assembly name)` + `AddSharedKernelHealthChecks()`. Registers no dependency-specific check.
- `builder.AddSharedKernelTelemetry(serviceName)` — tracing, metrics, logs, `BaggageLogRecordProcessor`; OTLP exporter configured only by the standard `OTEL_EXPORTER_OTLP_*` variables; installs `RequestBaggageRefusingPropagator`.
- `services.AddSharedKernelHealthChecks()` — `StartupGate` + `startup` check (tag `ready`). A second call duplicates `startup` and the host throws.
- `healthChecks.AddSharedKernelReadiness(o => …)` — one `ready` check per registered `IReadinessProbe`, named after the probe; a throwing probe → Unhealthy with the exception type only (13005); duplicate names throw.
- `app.MapDefaultHealthCheckEndpoints()` — `/health/live` (`live` tag) and `/health/ready` (`ready` tag).
- `StartupGate.MarkReady()`; `HealthCheckNames`, `HealthCheckTags`, `HealthCheckRegistrationLogging.LogRegistration` (public so a service's own check logs 13002).
- `builder.With{Application,Caching,Communication,Integration,Intelligence,Messaging,Persistence,Reporting,Scheduling,Search,Storage,Workflow}Telemetry()` — subscribe to each domain's `ActivitySource`/`Meter` **by name**; reference nothing; idempotent.
- `builder.AddSharedKernelRateLimiting(o => …)` — global fixed window per remote IP + `RateLimitPolicyNames.Authentication`; `configure` runs last.

**`.Security`** — `services.AddSharedKernelRequestContext(o => …)` (`RequestContextOptions.TrustInboundBaggage`, default `false`; needs an `IUserContext` from `12.Security`) and `app.UseSharedKernelRequestContext()` (the first middleware; throws if the services were not added).

**`.Persistence`** — `AddDatabaseReadinessCheck<TContext>()`, `AddDapperDatabaseReadinessCheck()`, `AddPersistenceStartupReadinessCheck()`; tagged `ready`, `db`.

**`SharedKernel.MultiTenancy`** — `services.AddSharedKernelMultiTenancy(o => …)` (`TenantResolutionOptions`, section `SharedKernel:MultiTenancy`) then `app.UseMiddleware<TenantResolutionMiddleware>()` after authentication. `Database` needs an `IDbConnectionFactory`; without one, an order that names it fails startup. Catalog: `ITenantCatalog`, `TenantDescriptor`, `DatabaseTenantCatalog`, `CachedTenantCatalog`, `CatalogTenantStatusValidator`.

**`.Security.Mtls`** — `builder.AddMtlsClientCertificate(ClientCertificateMode)`; `builder.AddMtlsForwardedHeaderCertificate(o => …)` (section `SharedKernel:ServiceDefaults:MtlsForwardedHeader`) + `app.UseMiddleware<MtlsForwardedHeaderMiddleware>()`.

**`.Configuration.KeyVault`** — `builder.AddSharedKernelKeyVaultConfiguration(vaultUri, credential?)`; an unreachable vault throws at startup.

**`.Localization`** — `builder.AddSharedKernelLocalization(o => …)` (default order `UserPreference → TenantDefault → AcceptLanguageHeader`); the service still calls `app.UseRequestLocalization()`.

**Canonical order** (compiled reference: `samples/Shop/Ordering/Shop.Ordering.Api/Program.cs`): `AddServiceDefaults()` → authentication (`12.Security`) → `AddSharedKernelRequestContext()` → optional `AddSharedKernelMultiTenancy()` / `AddSharedKernelRateLimiting()` → `AddHealthChecks().AddDatabaseReadinessCheck<T>().AddSharedKernelReadiness()` → the `WithXTelemetry()` the service needs. Pipeline: `UseSharedKernelRequestContext()` first → `UseSharedKernelWebApi(p => p.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()))` (or by hand: `UseExceptionHandler`, `UseAuthentication`, tenant middleware, `UseRateLimiter`, `UseAuthorization`) → `MapDefaultHealthCheckEndpoints()`.

## Rules & Invariants

1. **The base references `SharedKernel.Primitives` and OpenTelemetry only** (`CompositionBaseIsolationTests`, by assembly references and by project file). Anything needing another kernel package goes in a `SharedKernel.ServiceDefaults.{Capability}` package or behind a provider-registered `IReadinessProbe`.
2. **Never add a per-dependency readiness package or `Add*ReadinessCheck` for a provider.** The provider owns its probe; `AddSharedKernelReadiness()` maps it. A probe constructor is cheap and resolves clients inside `ProbeAsync`.
3. An integration package references the base plus what its own integration needs, **never another `ServiceDefaults.*` integration package** (`.Localization` → `SharedKernel.MultiTenancy`, for the tenant-default culture via `ITenantCatalog`, is the one sibling edge); XML docs name a sibling's types as `<c>Name</c>`, never `<see cref>`.
4. Readiness checks chain onto `services.AddHealthChecks()` — never a second `AddSharedKernelHealthChecks()`.
5. **Live ≠ ready.** Anything depending on an external system is tagged `ready`, never `live`. A backlog, queue depth or job count is data, never a failure.
6. Helpers an integration package needs from the base are public API, never `InternalsVisibleTo`.
7. **One request context.** `UseSharedKernelRequestContext()` is the only HTTP adapter that creates the correlation id and the request's `RequestContextScope`; it runs before `UseExceptionHandler()`. Never add a second correlation-id middleware; never read `Activity.Id`/`TraceId` as the correlation id.
8. The correlation id is kept only when `CorrelationIds.IsValid` accepts it, else `CorrelationIds.New()`; a rejected value is never logged (length only, 13007). It is echoed via `Response.OnStarting`, also on error responses.
9. `HttpRequestContext` reads the caller **lazily** from `SecurityRequestContext`, because the middleware runs before `UseAuthentication()`.
10. `AddSharedKernelRequestContext()` uses `Add` (not `TryAdd`) for `IRequestContext` so it replaces `Persistence.EfCore`'s fail-closed `AnonymousRequestContext` in any order. Mapping: `UserId = SubjectId ?? ClientId`; `TenantId` = `IUserContext.TenantId`; `ActorKind` from `IUserContext` when authenticated, else `Anonymous` (never `System`); `HasPermissionAsync` ordinal.
11. **Tenant resolution only replaces the tenant**, in an inner `RequestContextScope` (`WithTenant`); caller and correlation id stay the outer scope's. The middleware also sets `WellKnownBaggageKeys.TenantId` baggage.
12. **Tenant strategy order is a security default**: `[Claim, Header, Database]` puts the signed claim before the forgeable header. Never reorder without a security review. `StrategyOrder` defaults to empty (binding appends to a non-empty list); empty means `DefaultStrategyOrder`.
13. `TenantResolutionMiddleware` takes the scoped strategies per request (`InvokeAsync` parameter), never in its constructor — ASP.NET Core builds it once from the root provider.
14. The claim strategy resolves through `UserContextResolver.Resolve` and the registered mappers — never parse claims here. The header strategy uses `WellKnownHeaders.TenantId` and `TenantId.TryParse`; malformed → `null`, never throws.
15. **Fail closed on tenants.** Unresolved, inactive (`ITenantStatusValidator.IsActiveAsync` false) and catalog miss all mean `TenantId = null`. `ITenantStatusValidator` is optional.
16. `DatabaseTenantResolutionStrategy` and `DatabaseTenantCatalog` use parameterized SQL only, async with the `CancellationToken` threaded through.
17. `ITenantCatalog` is read-only — never add provisioning. `CachedTenantCatalog`'s TTL (30 s default) stays short; call `InvalidateTenantAsync` after a status change.
18. **`BaggageLogRecordProcessor` copies only the platform's own keys** (`PlatformBaggageKeys`, pinned by a test to `WellKnownBaggageKeys`: correlation id and tenant) and never a value with a control character or U+2028/U+2029; an explicit record attribute wins. A key joins the list only if platform middleware writes *and replaces* it.
19. **`Baggage.Current` is never filled from an incoming request**: `RequestBaggageRefusingPropagator` decorates the default propagator; HTTP-request extraction keeps trace context and drops baggage. Never remove it; never read a caller's identity from baggage. `TrustInboundBaggage` governs only the request `Activity`'s baggage.
20. **`AddSharedKernelRateLimiting()` never sets `OnRejected`**, is never called by `AddServiceDefaults()`, and never references `14.Presentation`; `AddSharedKernelWebApi()` fills `OnRejected` with the platform 429 only when nothing else has.
21. Keep `AddSharedKernelKeyVaultConfiguration()` (secrets as configuration) distinct from `AddAzureKeyVaultEncryption` (keys for encryption).
22. `requireAuthorization: true` on health endpoints is defense in depth only (it breaks kubelet probes); endpoints must be network-restricted.
23. mTLS: the validator is resolved per handshake from a fresh scope; `MtlsForwardedHeaderOptions.HeaderName` has no vendor default; without `TrustedNetworks` a one-time warning (13003) fires.
24. **Never log** certificate bytes, raw tokens, raw header values or a rejected correlation id — lengths and thumbprints only. *(planned, SK.13.HostObservabilityFixes)* Also never a certificate subject, a remote IP address, a tenant id placeholder or free-text reasons (a validator's `FailureReason`); a caught exception is passed as the `Exception` parameter, never as its type name or message.
25. New packages are named `SharedKernel.ServiceDefaults.{Capability}[.{Provider}]`.
26. `StartupGate` is the only mutable singleton state. OTel, health checks, rate limiting, strategies and middleware stay reflection-free.
27. *(planned, SK.13.WildcardTelemetry)* **Kernel telemetry is subscribed by wildcard, never by copied name.** `AddSharedKernelTelemetry()` calls `AddSource("SharedKernel.*")` and `AddMeter("SharedKernel.*")`; no `SharedKernel.`-named source or meter literal is copied into this domain. A kernel source or meter is named `SharedKernel.{Capability}[.{Sub}]`; a name outside that prefix is not exported (a defect of its owner, guarded by 00.Governance). `With{X}Telemetry()` exists only for third-party instrumentation (`MassTransit`, `Npgsql`, `Polly` with gRPC client instrumentation, the Temporal tracing sources), never for a name a kernel package emits itself.
28. *(planned, SK.13.WildcardTelemetry)* **One resource, one version.** `AddSharedKernelTelemetry()` configures the resource once; `service.version` is `TelemetryVersion.Of(entry assembly)` (informational version), `TelemetryVersion.Unknown` when there is no entry assembly. Second-unit `Histogram<double>` instruments of `SharedKernel.*` meters get the second-scale bucket view from the base (type-pattern predicate, no reflection).
29. *(planned, SK.13.HostObservabilityFixes)* **The inbound server span carries the platform's ids.** `RequestContextMiddleware` tags `Activity.Current` with `WellKnownTagKeys.CorrelationId`, and `TenantResolutionMiddleware` with `WellKnownTagKeys.TenantId` (a host without multi-tenancy gets the credential-asserted tenant from `RequestContextMiddleware`'s `finally`). Only validated, platform-made values are tagged, written before `UseExceptionHandler()` so a handled exception cannot remove them. This domain starts no span and owns no `ActivitySource`; it leaves the ASP.NET Core instrumentation's `RecordException` off (the exception handler and the gRPC interceptor of `14.Presentation` record the failure once).
30. *(planned, SK.13.HostObservabilityFixes)* **Request-context headers are read through `RequestContextPropagation.ReadHeaders`, never hand-parsed, and the identity they carry is never applied.** The correlation id is taken from the result; the parsed tenant, actor id, actor kind and client id must not reach `IRequestContext`, a span tag, a log or baggage (an inbound HTTP header is forgeable; rules 9-11, 19).
31. *(planned, SK.13.ServiceDefaultsConformance)* **One EventId sub-block per logging package** (table in `## Logging`), constants from `{Package}LoggingEventIds` over `LoggingEventIdRanges.ServiceDefaults`, one `{Package}Log` class per package under `Diagnostics/`, categories from kernel marker types (never a string, never a consumer type), required loggers, templates one sentence with quoted identifiers, `Exception` last on every caught-exception log. Tests name the domain base explicitly: `ServiceDefaults.Security.Mtls` is 13300 here, `Security.Mtls` is 12300 in `12.Security`.

## Decisions

| Decision | Why |
| --- | --- |
| Providers register `IReadinessProbe`; one `AddSharedKernelReadiness()` maps them | No per-dependency packages; the host cannot forget a dependency it configured |
| Base references Foundation only | Every service restores the base; heavy dependencies stay opt-in |
| `WithXTelemetry()` subscribe by source name | No reference from the base to any capability package. *(planned, SK.13.WildcardTelemetry: narrowed to the four third-party families; kernel names come from the wildcard)* |
| *(planned, SK.13.WildcardTelemetry)* `SharedKernel.*` wildcard in the base; eight `With{X}Telemetry()` deleted, no shims | A hand-copied name silently dropped the Messaging meter and Temporal sources; the prefix is the one contract. Pre-GA, so the deletion is a release-noted breaking change like the deleted readiness packages |
| *(planned, SK.13.WildcardTelemetry)* HttpClient metrics and SignalR server sources live in the base, not a new extension | The base already carries the HttpClient and ASP.NET Core instrumentation; an unused source costs nothing |
| Baggage allow-list on log records (correlation id, tenant only) | Copying every baggage item would let an anonymous caller stamp any property on every log record of its request |
| Inbound baggage refused by default at two stores (propagator + request `Activity`) | Baggage flows to every outbound call; a caller could plant a tenant or user id downstream services trust |
| No default `OnRejected` in rate limiting | A default here would override `14.Presentation`'s platform 429 problem body |
| Claim → Header → Database default order | A signature-verified claim outranks an unsigned header |
| Tenant catalog is read-only | Provisioning/onboarding is a service concern, not host composition |
| *(planned, SK.13.HostObservabilityFixes)* Request-context headers are parsed through `ReadHeaders`; the identity they carry is discarded | An inbound HTTP header is forgeable by any external caller, and traces share the trust level of logs; applying or tagging it would reopen the spoofing hole baggage refusal closed. An "on behalf of" attribution needs a security-reviewed opt-in and an `IRequestContext` member `01.Core` does not have |
| *(planned, SK.13.HostObservabilityFixes)* `tenant.id` is written by the tenant middleware when it resolves, else filled from the credential-asserted tenant | Keeps the tag correct with and without `SharedKernel.MultiTenancy` using no cross-package marker. Accepted imprecision: for an inactive tenant the span shows the asserted tenant while the request ran with `TenantId = null` |
| *(planned, SK.13.ServiceDefaultsConformance)* The request-context and tenant middleware tag the ambient server span and start no span | A per-request span in the hottest path with no failure semantic of its own earns nothing; T1 would also need a `SharedKernel.MultiTenancy` source for no reader. T1, T3, T5, T6, E1, E4, C1-C3 do not apply to this domain (no source, meter, `Error` or classified exception exists) |
| *(planned, SK.13.ServiceDefaultsConformance)* One sub-block per package, no package families | Seven packages fit the ten slots of 13000-13999; sharing a block is only needed where packages exceed slots (`01.Core`) |
| *(planned, SK.13.ServiceDefaultsConformance)* `UnavailableDatabaseTenantResolutionStrategy` stays a defensive stub | Startup already fails through `TenantResolutionOptionsValidator`; the stub is reachable only by bypassing it, and sharing one message constant keeps the two from drifting |

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

*(planned, SK.13.HostObservabilityFixes)* 13000/13001 log the thumbprint only (no subject, no reason text); **13008** (`.Security.Mtls`) is a forwarded certificate ignored because its source is outside `TrustedNetworks` (no parameters, no IP); 13005 passes the exception; 13100 loses `{TenantId}`. Next free becomes 13009.

*(planned, SK.13.ServiceDefaultsConformance)* One sub-block per package, replacing the shared 13000-13099 range. `SharedKernel.MultiTenancy` is unchanged; ids renumber once, in the release that carries both phases. Sub-block indexes are over `LoggingEventIdRanges.ServiceDefaults`; the unrelated 12.Security block (`Security.Mtls` 12300) never mixes with them.

| Index | Sub-block | Package | Events |
| :---: | --- | --- | --- |
| 0 | 13000-13099 | `SharedKernel.ServiceDefaults` | 13000 health check registered; 13001 readiness probe threw |
| 1 | 13100-13199 | `SharedKernel.MultiTenancy` | 13100 tenant resolved; 13101 tenant not resolved |
| 2 | 13200-13299 | `SharedKernel.ServiceDefaults.Security` | 13200 correlation id created; 13201 inbound correlation id rejected |
| 3 | 13300-13399 | `SharedKernel.ServiceDefaults.Security.Mtls` | 13300 accepted; 13301 rejected; 13302 forwarded certificate refused; 13303 forwarded-header trust not configured |
| 4 | 13400-13499 | `SharedKernel.ServiceDefaults.Localization` | 13400 no dynamic culture step can resolve |
| 5 | 13500-13599 | `SharedKernel.ServiceDefaults.Persistence` | reserved |
| 6 | 13600-13699 | `SharedKernel.ServiceDefaults.Configuration.KeyVault` | reserved |

## Cross-Domain Couplings

- **01.Core** — `SharedKernel.Primitives` (`IReadinessProbe`, `WellKnownHeaders`/`WellKnownBaggageKeys`), `SharedKernel.Execution` (`IRequestContext`, `RequestContextScope`, `CorrelationIds`, `TenantId`). Key Vault key provider lives in `SharedKernel.Cryptography.KeyVault.Azure`.
- **12.Security** — `IUserContext`/`IUserContextMapper`/`UserContextResolver` feed the request context, the claim strategy and localization's user-preference step; `.Security.Mtls` delegates acceptance to `IMtlsCertificateValidator` and forwards certificates Oidc's RFC 8705 check reads.
- **06.Persistence** — `.Persistence` reads `IPersistenceStartup` and `SharedKernelDbContext.CheckReadinessAsync`; `MultiTenancy` uses `IDbConnectionFactory`. `field-encryption`/`audit-sealing` are probes registered by persistence, mapped by `AddSharedKernelReadiness()`.
- **02.Caching** — `CachedTenantCatalog` over `ICacheService` (Abstractions only).
- **14.Presentation** — `UseSharedKernelWebApi()` composes the pipeline after `UseSharedKernelRequestContext()` and shapes the limiter's 429; this domain never references it.
- **07.Messaging** — MassTransit copies publisher baggage onto consume activities; the log allow-list keeps only the platform keys. A consumer's tenant/actor come from `WithInboundRequestContext()`, never baggage.
- **All capability domains** — *(planned, SK.13.WildcardTelemetry)* a kernel source/meter named `SharedKernel.{Capability}[.{Sub}]` is exported with no registration here; third-party names stay byte-identical: `MassTransit` (07), `Npgsql` (06), `Polly` + gRPC client (11), the Temporal tracing sources (17).
- **14.Presentation** — *(planned, SK.13.HostObservabilityFixes)* the request-context and tenant tags are written before `UseExceptionHandler()`, so `SuppressDiagnosticsCallback` cannot hide them; `RecordException` stays off here because the exception handler and the gRPC interceptor record the failure on the server span themselves; gRPC hosted in ASP.NET Core inherits the tags and the validated correlation id from this pipeline.
- **12.Security** — *(planned, SK.13.ServiceDefaultsConformance)* `Security.Mtls` is 12300 in 12.Security; `ServiceDefaults.Security.Mtls` is 13300 here. Never compare sub-block indexes without the domain base.
- **16.Testing** — *(planned)* `InMemoryLogger` does not capture the ambient request context (it arrives through the OTel baggage processor); ambient enrichment is asserted through the OTel in-memory exporter.
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
- Fakes: `SharedKernel.ServiceDefaults.Testing` — catalogue in `src/Testing/CLAUDE.md`.
- *(planned, SK.13.HostObservabilityFixes)* The server span exists only when something listens to the `Microsoft.AspNetCore` source: a tag test registers a listener, filters spans by trace id and joins the non-parallel collection. Forged identity headers, the tenant-tag matrix (with and without multi-tenancy, inactive tenant) and a handled exception are covered; log tests assert no subject, IP or tenant id with canary values.
- *(planned, SK.13.ServiceDefaultsConformance)* One integrity test reads the `## Logging` ownership table: ids unique, each inside its package's index over `LoggingEventIdRanges.ServiceDefaults`.

## Known Limitations

- Kestrel's client-certificate callback is synchronous, so `AddMtlsClientCertificate` bridges the async `IMtlsCertificateValidator` with a blocking wait — the validator must answer from memory.
- A default propagator set *after* the host starts removes the baggage-refusal protection (one set before is wrapped).
- With an asynchronous `IEncryptionKeyProvider`, persistence's ETag key is warmed by a hosted service (up to 10 s) that readiness does not wait for.
- Configuration-bound options and the Key Vault configuration provider are not trim/AOT-clean.
- *(planned, SK.13.HostObservabilityFixes)* Over HTTP the upstream actor is not visible to a downstream service: the actor and client headers clients write are parsed but deliberately not applied (no "on behalf of" attribution until a security-reviewed opt-in exists). For an inactive tenant the server span carries the credential-asserted tenant while the request context has none.
