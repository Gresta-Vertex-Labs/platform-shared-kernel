# 13.ServiceDefaults

Host composition layer for Platform.SharedKernel microservices.

- **`SharedKernel.ServiceDefaults`** — `AddServiceDefaults()` composition entry point; OpenTelemetry (tracing/metrics/logging) wiring; health check composition with a hard liveness/readiness split; opt-in dependency-specific health check adapters (database, Redis, cache, messaging, object storage, search, vector store, workflow service); startup-probe gating. Carries the platform's one documented layering exception permitting a `ProjectReference` to `17.Workflows`'s `SharedKernel.Workflows.Temporal` — scoped **exclusively** to `IWorkflowServiceProbe`/`WorkflowServiceHealth` for `AddWorkflowReadinessCheck` (WO-047); no other `17.Workflows` type may be reached through it.
- **`SharedKernel.MultiTenancy`** — concrete `ITenantProvider` resolution strategies (HTTP header, JWT claim delegation, database tenant-directory lookup); `TenantResolutionMiddleware`; `AmbientTenantProvider`.

Both packages are composition-only: they wire abstractions and concrete providers from layers `01`–`12` together. No business logic, no domain types, no new abstractions are defined here.

## Program.cs composition

```csharp
var builder = WebApplication.CreateBuilder(args);

// Secrets-manager configuration — optional; call BEFORE AddServiceDefaults() so vault-backed values
// are available to every subsequent registration. NEVER commit plaintext secrets to appsettings.json
// — see "Secrets-manager configuration" below.
builder.AddSharedKernelKeyVaultConfiguration(new Uri("https://my-vault.vault.azure.net/"));

builder.AddServiceDefaults();                          // OTel + base health endpoints — FIRST call
builder.Services.AddSharedKernelMultiTenancy();        // optional — multi-tenant services only; default
                                                        // StrategyOrder is [Claim, Header, Database] —
                                                        // security-motivated, see Ordering rule 2
builder.AddSharedKernelRateLimiting();                 // optional — see "Rate limiting" below

// Opt-in dependency-specific health checks — only what this service actually uses:
builder.Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<MyDbContext>()
    .AddRedisHealthCheck(redisConnectionString)
    .AddMessagingReadinessCheck()
    .AddStorageReadinessCheck("my-bucket")
    .AddSearchReadinessCheck("products-index")
    .AddVectorStoreReadinessCheck("documents-collection")
    .AddWorkflowReadinessCheck();

builder.WithMessagingTelemetry();                      // optional — services using 07.Messaging
builder.WithCachingTelemetry();                        // optional — services using 02.Caching
builder.WithApplicationTelemetry();                    // optional — services using 05.Application's pipeline behaviors
builder.WithSearchTelemetry();                         // optional — services using 09.Search
builder.WithIntelligenceTelemetry();                   // optional — services using 10.Intelligence
builder.WithWorkflowTelemetry();                       // optional — services using 17.Workflows
builder.WithPersistenceTelemetry();                    // optional — services using 06.Persistence (tracing only, no companion meter)
builder.WithCommunicationTelemetry();                  // optional — services making outbound gRPC and/or resilience-wrapped REST calls via 11.Communication
builder.WithIntegrationTelemetry();                    // optional — services dispatching webhooks via 15.Integration (tracing only, no companion meter)

// mTLS client-certificate composition — optional; pick the one matching this service's TLS-termination
// topology (a host MAY register both if its topology genuinely varies by environment). Both require
// 12.Security's IMtlsCertificateValidator to already be registered (typically AddMtlsAuthentication<TValidator>()).
builder.AddMtlsClientCertificate(ClientCertificateMode.RequireCertificate);        // TLS terminates directly at Kestrel
// -- or, for TLS that terminates at an ingress/gateway which forwards the client certificate as a header --
builder.AddMtlsForwardedHeaderCertificate(o => o.HeaderName = "ssl-client-cert");  // header name must match the actual ingress — never a platform-guessed default
// -- with the trust-boundary allowlist restricted to the known ingress/proxy addresses (recommended in
// production — an unconfigured TrustedNetworks accepts the forwarded header from ANY network path that
// reaches this host directly; see "mTLS forwarded-header trust boundary" below) --
builder.AddMtlsForwardedHeaderCertificate(o =>
{
    o.HeaderName = "ssl-client-cert";
    o.AddTrustedNetwork(IPNetwork.Parse("10.0.0.0/16"));   // the ingress-controller subnet
    o.AddTrustedProxy(IPAddress.Parse("10.0.5.7"));        // a single known proxy hop
});

var app = builder.Build();

app.UseAuthentication();
app.UseRateLimiter();                                  // required when AddSharedKernelRateLimiting() is used
app.UseMiddleware<TenantResolutionMiddleware>();       // required when AddSharedKernelMultiTenancy() is used — must run after UseAuthentication()
app.UseMiddleware<MtlsForwardedHeaderMiddleware>();    // required when AddMtlsForwardedHeaderCertificate() is used — registering the options alone leaves this absent from the pipeline (silent no-op, not a crash)

// requireAuthorization: true adds .RequireAuthorization() to both endpoint mappings — DEFENSE IN DEPTH
// ONLY, NEVER A SUBSTITUTE FOR NETWORK ISOLATION. See "Health endpoint exposure" below.
app.MapDefaultHealthCheckEndpoints();                  // "/health/live", "/health/ready"

app.Run();
```

### Ordering rules

1. `builder.AddServiceDefaults()` must be the **first** call in `Program.cs`, before any other `SharedKernel.*.Add...` extension. It wires OpenTelemetry and registers only the base health check infrastructure (the always-on `StartupGateHealthCheck` plus the `/health/live` and `/health/ready` endpoint mappings) — it never registers a dependency-specific check.
2. `AddSharedKernelMultiTenancy()` is optional and only needed by multi-tenant services. It registers `TenantResolutionOptions`, `AmbientTenantProvider` (scoped `ITenantProvider`), and the three platform `ITenantResolutionStrategy` implementations — but **not** the middleware itself. `TenantResolutionOptions.StrategyOrder` defaults to `[Claim, Header, Database]` (WO-061/P-393) — **SECURITY-MOTIVATED, do not reorder back to `[Header, Claim, Database]` without a security review.** The prior default let an unsigned, caller-supplied `X-Tenant-Id` header outrank a cryptographically-verified JWT tenant claim for the same request, since the middleware takes the first strategy that resolves and stops; putting `Claim` first closes that vector, and the reorder is provably safe for the pre-existing B2B/API-key header-only path because `ClaimTenantResolutionStrategy` returns `null` for any unauthenticated request or a token with no tenant claim. `AddSharedKernelMultiTenancy()` also registers `TenantResolutionOptionsValidator` via `.ValidateOnStart()` — a `StrategyOrder` entry naming an unregistered strategy (a typo, or a strategy whose registration was removed) fails fast at `IHost.StartAsync()` instead of silently resolving every request to `Guid.Empty`.
3. Every dependency-specific health check (`AddDatabaseReadinessCheck<TContext>`, `AddDapperDatabaseReadinessCheck`, `AddRedisHealthCheck`, `AddCacheReadinessCheck`, `AddMessagingReadinessCheck`, `AddStorageReadinessCheck`, `AddSearchReadinessCheck`, `AddVectorStoreReadinessCheck`, `AddWorkflowReadinessCheck`) is an explicit opt-in call on the `IHealthChecksBuilder` returned by `services.AddHealthChecks()`. A service only registers the checks for dependencies it actually uses. `AddStorageReadinessCheck(bucket)` requires `bucket`, `AddSearchReadinessCheck(indexName)` requires `indexName`, and `AddVectorStoreReadinessCheck(collectionName)` requires `collectionName`, as explicit arguments — all three are deliberately never defaulted from a provider's own options type (`SharedKernel.Storage.S3`/`.Obs`'s `DefaultBucket`, `SharedKernel.Search.Meilisearch`/`.ElasticSearch`'s index configuration, or `SharedKernel.AI.Qdrant`/`.Milvus`'s collection configuration), since that would reintroduce the provider-specific coupling these methods exist to avoid. Each resolves only its neutral abstraction (`IFileStorage`, `ISearchIndexProvisioner`, `IVectorCollectionProvisioner`) from DI and works uniformly against whichever provider is registered. `AddWorkflowReadinessCheck()` and `AddMessagingReadinessCheck()` take **no** identifier argument — `IWorkflowServiceProbe`/`IMessageBusProbe` are both per-host singletons with nothing analogous to a bucket/index/collection name to disambiguate; `AddMessagingReadinessCheck()` specifically resolves `07.Messaging`'s `IMessageBusProbe` from DI (registered unconditionally by `MessagingBusBuilder.Build()`), reflecting the real, already-configured bus rather than opening a second, independent connection. `AddSearchReadinessCheck`/`AddVectorStoreReadinessCheck`/`AddWorkflowReadinessCheck`/`AddMessagingReadinessCheck` all report `Unhealthy` — never `Degraded` — unless every one of their probe's boolean signals (`Reachable`+`IndexAddressable`+`Searchable`; `Reachable`+`CollectionAddressable`+`Queryable`; `Reachable`+`NamespaceAddressable`+`WorkerPollersActive`; `IsHealthy`, respectively) report `true`; a deep write/task backlog (`PendingWriteCount`/`TaskQueueBacklog`) is surfaced only as informational `HealthCheckResult.Data` and never fails the check, since it means results are stale or work is slow, not that the dependency is unavailable.

   **Migration note (WO-054/P-351, breaking change):** `AddRabbitMqMessagingHealthCheck(amqpUri)` and `AddAzureServiceBusMessagingHealthCheck(connectionStringOrNamespace)` have been **removed outright, not deprecated** — no signature-compatible replacement exists. Both independently constructed a second connection from a caller-supplied connection string, entirely disconnected from whatever `07.Messaging.MassTransit`'s `MessagingBusBuilder` actually configured for the service — a health check that could pass while the real bus was down, or fail while it was healthy. Replace either call with `.AddMessagingReadinessCheck()` (no arguments — it resolves `IMessageBusProbe` from DI, which already reflects the real, already-configured bus).
4. `WithMessagingTelemetry()` / `WithCachingTelemetry()` / `WithApplicationTelemetry()` / `WithSearchTelemetry()` / `WithIntelligenceTelemetry()` / `WithWorkflowTelemetry()` / `WithPersistenceTelemetry()` / `WithCommunicationTelemetry()` / `WithIntegrationTelemetry()` are all optional. The first seven only wire already-existing `ActivitySource`/`Meter` instruments owned by `07.Messaging`, `02.Caching`, `05.Application.Behaviors`, `09.Search`, `10.Intelligence`, `17.Workflows`, and `06.Persistence` respectively into this host's `TracerProvider`/`MeterProvider`, by bare string name, with no `ProjectReference` to their owning domain. `WithPersistenceTelemetry()` and `WithIntegrationTelemetry()` are the two exceptions among those to the "wires both tracing and metrics" pattern most of their siblings share: `06.Persistence` and `15.Integration` each ship only an `ActivitySource` (for repository-operation spans and webhook-dispatch spans respectively), no companion `Meter`, so both methods call `WithTracing(...)` only — a deliberate scope decision (D-16 for persistence, D-29 for integration), not an oversight, and each will gain a `WithMetrics(...)` call only if its owning domain ships a corresponding meter in a future phase. `WithIntegrationTelemetry()` wires `15.Integration`'s `"SharedKernel.Integration"` `ActivitySource` (`WebhookIntegrationActivitySource`, WO-064/P-424) — again by bare string name, with no `ProjectReference` to `SharedKernel.Integration.Webhooks`. `WithCommunicationTelemetry()` — the eighth sibling — is architecturally distinct from the rest: `11.Communication` owns no `"SharedKernel.Communication"` instrumentation source of its own to wire by string name, so this method instead activates two independent **third-party** OTel integrations already referenced transitively inside `11.Communication` but never invoked by anything: gRPC client tracing (`OpenTelemetry.Instrumentation.GrpcNetClient` — the family's first member requiring its own new `PackageReference` on this package, since there is no `"SharedKernel.Communication"` source to reach by a bare `AddSource` call) and Polly v8's own `"Polly"`-named resilience `Meter` (retry/circuit-breaker/timeout telemetry — **metrics only**; Polly v8.4.2, the version pinned transitively by `Microsoft.Extensions.Http.Resilience 10.7.0`, was confirmed by decompilation to emit no corresponding `ActivitySource`, so there is no `WithTracing(AddSource("Polly"))` call). `WithCommunicationTelemetry()` is purely additive to the baseline HTTP spans `OpenTelemetry.Instrumentation.Http` already produces unconditionally inside `AddSharedKernelTelemetry` — never a replacement. All nine methods are idempotent — calling any of them more than once registers no duplicate instrument.
5. `app.UseMiddleware<TenantResolutionMiddleware>()` is **required** whenever `AddSharedKernelMultiTenancy()` is used, and **must** be placed after `app.UseAuthentication()` — `ClaimTenantResolutionStrategy` needs a populated `HttpContext.User`. Without this call, `AmbientTenantProvider.TenantId` stays permanently `Guid.Empty` (a silent, by-design failure mode, not a crash).
6. `app.MapDefaultHealthCheckEndpoints(bool requireAuthorization = false)` maps `/health/live` (only `"live"`-tagged checks — process-alive signal only) and `/health/ready` (only `"ready"`-tagged checks — may depend on DB/cache/broker connectivity, gates load-balancer rotation, never restarts the pod). `requireAuthorization: true` chains `.RequireAuthorization()` onto both mappings — see "Health endpoint exposure" below for the required network-isolation guidance and a worked example.
7. `AddMtlsClientCertificate()` / `AddMtlsForwardedHeaderCertificate()` are both optional and cover two mutually-exclusive TLS-termination topologies — a host MAY register both if its actual deployment genuinely varies by environment. `AddMtlsClientCertificate(mode)` is for hosts where TLS terminates directly at Kestrel: it wires `KestrelServerOptions.ConfigureHttpsDefaults` and needs no separate middleware registration. `AddMtlsForwardedHeaderCertificate(configure)` is for hosts where TLS terminates at an ingress/gateway that forwards the client certificate as a request header instead — it registers `MtlsForwardedHeaderOptions` (with `HeaderName` **required**, no platform default, since nginx-ingress/Envoy/Istio/HAProxy each use a different header name/encoding) and must be paired with an explicit `app.UseMiddleware<MtlsForwardedHeaderMiddleware>()` call, mirroring `AddSharedKernelMultiTenancy()`'s "register services here, wire the middleware separately" split. Neither surface reimplements X.509 chain/revocation validation — both delegate the accept/reject decision to `12.Security`'s `SharedKernel.Security.Mtls.IMtlsCertificateValidator`, which must already be registered (typically via `AddMtlsAuthentication<TValidator>()`); omitting it throws at the first TLS handshake (Kestrel path) or first request (forwarded-header path), not at startup. Because Kestrel's `ClientCertificateValidation` delegate is synchronous but `IMtlsCertificateValidator.ValidateAsync` is async-only, `AddMtlsClientCertificate` bridges the two with a blocking `.GetAwaiter().GetResult()` call inside the TLS handshake — a real latency/thread-pool-starvation cost under load, so a validator used on this path must resolve quickly (an in-memory allow-list or a cached trust decision) and must never make a slow remote call (CRL/OCSP, an external policy service). A host that calls neither method is byte-identical in behavior to today — Kestrel's default `ClientCertificateMode.NoCertificate` stays untouched and no new middleware enters the pipeline.
8. `AddSharedKernelRateLimiting()` is optional. When used, `app.UseRateLimiter()` must also be added to the pipeline (typically before the tenant/mTLS middleware, so a rejected request never reaches them) — see "Rate limiting" below.

### mTLS forwarded-header trust boundary

`MtlsForwardedHeaderOptions.TrustedNetworks` (WO-061/P-394) is an opt-in allowlist of the IP networks permitted to set the forwarded client-certificate header, mirroring ASP.NET Core's own `ForwardedHeadersOptions.KnownProxies`/`KnownNetworks` shape (unified into one `IPNetwork`-based collection, since `System.Net.IPNetwork` — BCL since .NET 8 — already expresses a single trusted proxy as a `/32`/`/128` network):

```csharp
builder.AddMtlsForwardedHeaderCertificate(o =>
{
    o.HeaderName = "ssl-client-cert";
    o.AddTrustedNetwork(IPNetwork.Parse("10.0.0.0/16"));   // the ingress-controller subnet
    o.AddTrustedProxy(IPAddress.Parse("10.0.5.7"));        // a single known proxy hop
});
```

When `TrustedNetworks` is non-empty, `MtlsForwardedHeaderMiddleware` ignores — never decodes, validates, or sets `HttpContext.Connection.ClientCertificate` for — a forwarded header from a remote IP outside the allowlist, regardless of whether the certificate itself would otherwise validate. **Leaving `TrustedNetworks` unconfigured (the default, shown in the unconfigured example above) preserves the pre-P-394 unrestricted behavior** — any network path that reaches this host directly (a misconfigured `NetworkPolicy`, a multi-hop mesh topology, a debug port, a compromised sidecar) can forge the header identically to the real ingress. A one-time startup `Warning` log (`ServiceDefaultsLog.ForwardedHeaderTrustBoundaryUnconfigured`) states this whenever `TrustedNetworks` is left empty. Configuring `TrustedNetworks` is recommended in production for the same reason `KnownProxies`/`KnownNetworks` is recommended for ASP.NET Core's own `UseForwardedHeaders()`.

### Rate limiting

`AddSharedKernelRateLimiting()` wraps ASP.NET Core's own built-in `Microsoft.AspNetCore.RateLimiting` middleware (no new NuGet dependency) with a conservative default: a global fixed-window limiter partitioned by remote IP (100 requests/minute), plus a named `RateLimitPolicyNames.Authentication` policy (10 requests/minute) a consumer attaches to its own token/login routes:

```csharp
builder.AddSharedKernelRateLimiting();
// ...
app.UseRateLimiter();

app.MapPost("/auth/token", TokenEndpoint)
    .RequireRateLimiting(RateLimitPolicyNames.Authentication);
```

Both thresholds are deliberately conservative defaults, not a tuned-for-every-service policy — a consuming service that needs per-endpoint or per-tenant tuning passes its own `configure` delegate (invoked **last**, after this method's own defaults, so it can override any threshold or add further named policies):

```csharp
builder.AddSharedKernelRateLimiting(options =>
{
    options.AddFixedWindowLimiter("bulk-export", policy =>
    {
        policy.PermitLimit = 5;
        policy.Window = TimeSpan.FromMinutes(1);
    });
});
```

This domain never references `14.Presentation` — `RateLimiterOptions.OnRejected` is left at the BCL default (a bare `429`, no response body) unless a consumer supplies one. A service that also uses `14.Presentation.WebApi` and wants a consistent RFC 9457 `ProblemDetails` rejection body attaches its own `OnRejected` delegate via `configure`, in the SERVICE's own composition root — never a hard `ProjectReference` from `13.ServiceDefaults` to `14.Presentation` — and calls that package's `RateLimitRejectionProblemDetails.Create(HttpContext, TimeSpan?)` helper to shape it. **This is the sanctioned way to build the rejection body — never hand-roll a raw `ProblemDetails` literal here**, mirroring `14.Presentation/CLAUDE.md`'s own "Rate-limit rejection bridge rules" (WO-062/P-408), which forbid exactly that construction pattern for every other 429 response on this platform:

```csharp
using System.Threading.RateLimiting;
using SharedKernel.Presentation.WebApi.RateLimiting;

builder.AddSharedKernelRateLimiting(options =>
{
    options.OnRejected = async (context, ct) =>
    {
        // Recover the limiter's own suggested delay from the rejected lease (BCL
        // System.Threading.RateLimiting.MetadataName) rather than inventing one.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterMetadata)
            ? retryAfterMetadata
            : (TimeSpan?)null;

        // The only sanctioned way to shape a rate-limit rejection into ProblemDetails — sets
        // Status/Type/Extensions["traceId"] identically to every other error path, and — because
        // retryAfter is supplied — also sets the real Retry-After response header itself.
        var problemDetails = RateLimitRejectionProblemDetails.Create(context.HttpContext, retryAfter);

        await context.HttpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: ct);
    };
});
```

`RateLimitRejectionProblemDetails.Create` sets the `Retry-After` HTTP response header itself (in whole seconds) whenever `retryAfter` is non-null, so proxies and client SDKs that already understand `Retry-After` work unmodified — the raw hand-rolled `ProblemDetails` this recipe replaced never set that header at all. This recipe is proven by a genuine compiled test (`RateLimitRejectionRecipeTests`, `SharedKernel.ServiceDefaults.Tests`) driving a real host through both the rejected request (429, `application/problem+json`, a `RateLimitRejectionProblemDetails`-shaped body, a parseable `Retry-After` header) and the no-recipe call shape (still the byte-identical BCL default — empty body, no `Retry-After`), via a **test-only** `ProjectReference` from the test project to `SharedKernel.Presentation.WebApi` — the production `SharedKernel.ServiceDefaults.csproj` takes no reference to `14.Presentation` in either direction.

### Secrets-manager configuration

`AddSharedKernelKeyVaultConfiguration(vaultUri, credential?)` adds Azure Key Vault as an additional `IConfiguration` source, wrapping `Azure.Extensions.AspNetCore.Configuration.Secrets`:

```csharp
builder.AddSharedKernelKeyVaultConfiguration(new Uri("https://my-vault.vault.azure.net/"));
// -- or with an explicit credential instead of the DefaultAzureCredential fallback --
builder.AddSharedKernelKeyVaultConfiguration(new Uri("https://my-vault.vault.azure.net/"), new ManagedIdentityCredential());
```

**NEVER COMMIT PLAINTEXT SECRETS (CONNECTION STRINGS, API KEYS, SIGNING KEYS) TO `appsettings.json` OR ANY FILE TRACKED BY SOURCE CONTROL.** Use this method (or an environment-appropriate equivalent) to source secret values from a real secrets manager instead. A misconfigured or unreachable vault throws synchronously from this call, at startup — `ConfigurationManager` (the concrete type behind `IHostApplicationBuilder.Configuration` for both `WebApplicationBuilder` and `Host.CreateApplicationBuilder`) rebuilds its `IConfigurationRoot` eagerly on every `Add(...)` call, unlike the classic deferred-`Build()` `ConfigurationBuilder` pattern, so there is no silent "empty configuration source" fallback to guard against.

Azure Key Vault is the first of a deliberately pluggable secrets-provider family (mirroring `08.Storage`'s S3/OBS multi-cloud precedent) — chosen first because `12.Security.Oidc` already targets Azure B2C, so `TokenCredential`/`DefaultAzureCredential` is already first-class in this platform's identity story. AWS Secrets Manager and HashiCorp Vault are explicit, documented future providers, not yet implemented.

### Health endpoint exposure

**`/health/live` AND `/health/ready` MUST BE NETWORK-RESTRICTED AT THE INGRESS/`NETWORKPOLICY` LAYER IN ANY ENVIRONMENT WHERE THEY ARE NOT INTENTIONALLY PUBLIC, INDEPENDENT OF WHETHER `requireAuthorization` IS USED.** `MapDefaultHealthCheckEndpoints(requireAuthorization: true)` is defense-in-depth only, never a substitute for network isolation — a Kubernetes kubelet's own liveness/readiness probe calls are typically unauthenticated, so enabling this parameter on the endpoint set the kubelet itself calls will cause the kubelet's own probes to be rejected. Reserve `requireAuthorization: true` for a deployment where these endpoints are deliberately reachable by something other than the kubelet (e.g. an external status dashboard behind its own auth), and gate the kubelet's own probe calls separately (a second, unauthenticated endpoint set, or an ingress rule that exempts the kubelet's source network).

When health data is exposed to an external audience, prefer a minimal `ResponseWriter` that serializes only the overall status — never the default `HealthReport.Entries[*].Data`/`.Description`, which can leak dependency version/connection details:

```csharp
app.MapDefaultHealthCheckEndpoints();

app.MapHealthChecks("/health/status", new HealthCheckOptions
{
    Predicate = _ => false,   // aggregate status only — evaluates no individual checks
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync($$"""{"status":"{{report.Status}}"}""");
    },
});
```

### Tenant-status validation bridge (`ITenantStatusValidator`)

`ITenantStatusValidator` (WO-061/P-400) is a locally-owned, opt-in seam — mirroring `05.Application.Behaviors`'s `IAuthorizationContext`/`IUnitOfWork` bridge pattern — letting `TenantResolutionMiddleware` reject an otherwise-resolved tenant whose status is suspended/offboarded, without this package taking a direct dependency on any specific tenant directory or cache technology:

```csharp
// ITenantStatusValidator — bridge to the consuming service's own tenant directory/cache.
// Never a direct 06.Persistence/02.Caching reference from SharedKernel.MultiTenancy itself.
services.AddScoped<SharedKernel.MultiTenancy.Resolution.ITenantStatusValidator, SqlTenantStatusValidator>();

public sealed class SqlTenantStatusValidator(ITenantDirectoryReadService directory) : ITenantStatusValidator
{
    public async Task<bool> IsActiveAsync(Guid tenantId, CancellationToken ct) =>
        await directory.GetTenantStatusAsync(tenantId, ct) is TenantStatus.Active;
}
```

Resolved as an **optional** DI service — `null` means "not registered," and the check is skipped entirely (zero added latency, zero behavior change) unless a consuming service registers an implementation. A `false` result is treated identically to "no strategy resolved a tenant" (the existing `Guid.Empty` fail-closed path), so an inactive tenant is deliberately indistinguishable from an absent one to every downstream consumer. Services using `DatabaseTenantResolutionStrategy` for DB-per-tenant/schema-per-tenant isolation already get equivalent protection by construction — its directory lookup IS an existence check — so registering `ITenantStatusValidator` alongside it is redundant but harmless, never a behavior conflict.

### Automatic log export and ambient TenantId/CorrelationId enrichment

`builder.AddServiceDefaults()` (via `AddSharedKernelTelemetry`) automatically exports every
`[LoggerMessage]`-authored log record through the same OTLP pipeline as traces and metrics —
`IncludeScopes` and `IncludeFormattedMessage` are both enabled, and a `BaggageLogRecordProcessor`
copies every `System.Diagnostics.Activity` baggage entry from `Activity.Current` onto each log
record's attributes at export time. No application-code call-site changes are needed to get this.

`BaggageLogRecordProcessor` is a **generic** mechanism — it carries no hardcoded baggage key
names. This is what makes it automatically pick up:

- `14.Presentation`'s correlation-id middleware, which sets its own `Activity` baggage key directly
  against the BCL (WO-031) — with **zero** `ProjectReference` from `13.ServiceDefaults` to
  `14.Presentation`.
- `SharedKernel.MultiTenancy`'s `TenantResolutionMiddleware`, which — when
  `AddSharedKernelMultiTenancy()` is used — sets `TenantBaggageKeys.TenantId` as `Activity` baggage
  immediately after resolving (or confirming `Guid.Empty` for) the current request's tenant. The
  baggage value is set even when no tenant resolves, so log aggregation can distinguish "no tenant
  resolved for this request" from "TenantId enrichment was never wired."

Any future domain that sets its own `Activity` baggage key gets the same free ambient-log
enrichment — no `13.ServiceDefaults` change required.

**Scope boundary:** this enrichment mechanism covers the HTTP-request path only, via whatever sets
`Activity` baggage during that request. A message-consumption-scope equivalent (e.g. a MassTransit
consumer filter setting the same baggage keys from propagated message headers) is **not**
implemented here — it would be a future `07.Messaging`-owned follow-up, outside this domain's
jurisdiction to dispatch.

See `13.ServiceDefaults/CLAUDE.md` for the full interface contracts, tag taxonomy, and implementation rules.
