# 13.ServiceDefaults

Host composition layer for Platform.SharedKernel microservices.

- **`SharedKernel.ServiceDefaults`** — `AddServiceDefaults()` composition entry point; OpenTelemetry (tracing/metrics/logging) wiring; health check composition with a hard liveness/readiness split; opt-in dependency-specific health check adapters (database, Redis, cache, messaging, object storage, search, vector store, workflow service); startup-probe gating. Carries the platform's one documented layering exception permitting a `ProjectReference` to `17.Workflows`'s `SharedKernel.Workflows.Temporal` — scoped **exclusively** to `IWorkflowServiceProbe`/`WorkflowServiceHealth` for `AddWorkflowReadinessCheck` (WO-047); no other `17.Workflows` type may be reached through it.
- **`SharedKernel.MultiTenancy`** — concrete `ITenantProvider` resolution strategies (HTTP header, JWT claim delegation, database tenant-directory lookup); `TenantResolutionMiddleware`; `AmbientTenantProvider`.

Both packages are composition-only: they wire abstractions and concrete providers from layers `01`–`12` together. No business logic, no domain types, no new abstractions are defined here.

## Program.cs composition

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                          // OTel + base health endpoints — FIRST call
builder.Services.AddSharedKernelMultiTenancy();        // optional — multi-tenant services only

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

// mTLS client-certificate composition — optional; pick the one matching this service's TLS-termination
// topology (a host MAY register both if its topology genuinely varies by environment). Both require
// 12.Security's IMtlsCertificateValidator to already be registered (typically AddMtlsAuthentication<TValidator>()).
builder.AddMtlsClientCertificate(ClientCertificateMode.RequireCertificate);        // TLS terminates directly at Kestrel
// -- or, for TLS that terminates at an ingress/gateway which forwards the client certificate as a header --
builder.AddMtlsForwardedHeaderCertificate(o => o.HeaderName = "ssl-client-cert");  // header name must match the actual ingress — never a platform-guessed default

var app = builder.Build();

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();       // required when AddSharedKernelMultiTenancy() is used — must run after UseAuthentication()
app.UseMiddleware<MtlsForwardedHeaderMiddleware>();    // required when AddMtlsForwardedHeaderCertificate() is used — registering the options alone leaves this absent from the pipeline (silent no-op, not a crash)

app.MapDefaultHealthCheckEndpoints();                  // "/health/live", "/health/ready"

app.Run();
```

### Ordering rules

1. `builder.AddServiceDefaults()` must be the **first** call in `Program.cs`, before any other `SharedKernel.*.Add...` extension. It wires OpenTelemetry and registers only the base health check infrastructure (the always-on `StartupGateHealthCheck` plus the `/health/live` and `/health/ready` endpoint mappings) — it never registers a dependency-specific check.
2. `AddSharedKernelMultiTenancy()` is optional and only needed by multi-tenant services. It registers `TenantResolutionOptions`, `AmbientTenantProvider` (scoped `ITenantProvider`), and the three platform `ITenantResolutionStrategy` implementations — but **not** the middleware itself.
3. Every dependency-specific health check (`AddDatabaseReadinessCheck<TContext>`, `AddDapperDatabaseReadinessCheck`, `AddRedisHealthCheck`, `AddCacheReadinessCheck`, `AddMessagingReadinessCheck`, `AddStorageReadinessCheck`, `AddSearchReadinessCheck`, `AddVectorStoreReadinessCheck`, `AddWorkflowReadinessCheck`) is an explicit opt-in call on the `IHealthChecksBuilder` returned by `services.AddHealthChecks()`. A service only registers the checks for dependencies it actually uses. `AddStorageReadinessCheck(bucket)` requires `bucket`, `AddSearchReadinessCheck(indexName)` requires `indexName`, and `AddVectorStoreReadinessCheck(collectionName)` requires `collectionName`, as explicit arguments — all three are deliberately never defaulted from a provider's own options type (`SharedKernel.Storage.S3`/`.Obs`'s `DefaultBucket`, `SharedKernel.Search.Meilisearch`/`.ElasticSearch`'s index configuration, or `SharedKernel.AI.Qdrant`/`.Milvus`'s collection configuration), since that would reintroduce the provider-specific coupling these methods exist to avoid. Each resolves only its neutral abstraction (`IFileStorage`, `ISearchIndexProvisioner`, `IVectorCollectionProvisioner`) from DI and works uniformly against whichever provider is registered. `AddWorkflowReadinessCheck()` and `AddMessagingReadinessCheck()` take **no** identifier argument — `IWorkflowServiceProbe`/`IMessageBusProbe` are both per-host singletons with nothing analogous to a bucket/index/collection name to disambiguate; `AddMessagingReadinessCheck()` specifically resolves `07.Messaging`'s `IMessageBusProbe` from DI (registered unconditionally by `MessagingBusBuilder.Build()`), reflecting the real, already-configured bus rather than opening a second, independent connection. `AddSearchReadinessCheck`/`AddVectorStoreReadinessCheck`/`AddWorkflowReadinessCheck`/`AddMessagingReadinessCheck` all report `Unhealthy` — never `Degraded` — unless every one of their probe's boolean signals (`Reachable`+`IndexAddressable`+`Searchable`; `Reachable`+`CollectionAddressable`+`Queryable`; `Reachable`+`NamespaceAddressable`+`WorkerPollersActive`; `IsHealthy`, respectively) report `true`; a deep write/task backlog (`PendingWriteCount`/`TaskQueueBacklog`) is surfaced only as informational `HealthCheckResult.Data` and never fails the check, since it means results are stale or work is slow, not that the dependency is unavailable.

   **Migration note (WO-054/P-351, breaking change):** `AddRabbitMqMessagingHealthCheck(amqpUri)` and `AddAzureServiceBusMessagingHealthCheck(connectionStringOrNamespace)` have been **removed outright, not deprecated** — no signature-compatible replacement exists. Both independently constructed a second connection from a caller-supplied connection string, entirely disconnected from whatever `07.Messaging.MassTransit`'s `MessagingBusBuilder` actually configured for the service — a health check that could pass while the real bus was down, or fail while it was healthy. Replace either call with `.AddMessagingReadinessCheck()` (no arguments — it resolves `IMessageBusProbe` from DI, which already reflects the real, already-configured bus).
4. `WithMessagingTelemetry()` / `WithCachingTelemetry()` / `WithApplicationTelemetry()` / `WithSearchTelemetry()` / `WithIntelligenceTelemetry()` / `WithWorkflowTelemetry()` / `WithPersistenceTelemetry()` / `WithCommunicationTelemetry()` are all optional. The first seven only wire already-existing `ActivitySource`/`Meter` instruments owned by `07.Messaging`, `02.Caching`, `05.Application.Behaviors`, `09.Search`, `10.Intelligence`, `17.Workflows`, and `06.Persistence` respectively into this host's `TracerProvider`/`MeterProvider`, by bare string name, with no `ProjectReference` to their owning domain. `WithPersistenceTelemetry()` is the sole exception among those seven to the "wires both tracing and metrics" pattern its siblings share: `06.Persistence` ships only an `ActivitySource` for repository-operation spans, no companion `Meter`, so this method calls `WithTracing(...)` only — this is a deliberate scope decision (D-16), not an oversight, and will gain a `WithMetrics(...)` call only if `06.Persistence` ships a corresponding meter in a future phase. `WithCommunicationTelemetry()` — the eighth sibling — is architecturally distinct from all seven: `11.Communication` owns no `"SharedKernel.Communication"` instrumentation source of its own to wire by string name, so this method instead activates two independent **third-party** OTel integrations already referenced transitively inside `11.Communication` but never invoked by anything: gRPC client tracing (`OpenTelemetry.Instrumentation.GrpcNetClient` — the family's first member requiring its own new `PackageReference` on this package, since there is no `"SharedKernel.Communication"` source to reach by a bare `AddSource` call) and Polly v8's own `"Polly"`-named resilience `Meter` (retry/circuit-breaker/timeout telemetry — **metrics only**; Polly v8.4.2, the version pinned transitively by `Microsoft.Extensions.Http.Resilience 10.7.0`, was confirmed by decompilation to emit no corresponding `ActivitySource`, so there is no `WithTracing(AddSource("Polly"))` call). `WithCommunicationTelemetry()` is purely additive to the baseline HTTP spans `OpenTelemetry.Instrumentation.Http` already produces unconditionally inside `AddSharedKernelTelemetry` — never a replacement. All eight methods are idempotent — calling any of them more than once registers no duplicate instrument.
5. `app.UseMiddleware<TenantResolutionMiddleware>()` is **required** whenever `AddSharedKernelMultiTenancy()` is used, and **must** be placed after `app.UseAuthentication()` — `ClaimTenantResolutionStrategy` needs a populated `HttpContext.User`. Without this call, `AmbientTenantProvider.TenantId` stays permanently `Guid.Empty` (a silent, by-design failure mode, not a crash).
6. `app.MapDefaultHealthCheckEndpoints()` maps `/health/live` (only `"live"`-tagged checks — process-alive signal only) and `/health/ready` (only `"ready"`-tagged checks — may depend on DB/cache/broker connectivity, gates load-balancer rotation, never restarts the pod).
7. `AddMtlsClientCertificate()` / `AddMtlsForwardedHeaderCertificate()` are both optional and cover two mutually-exclusive TLS-termination topologies — a host MAY register both if its actual deployment genuinely varies by environment. `AddMtlsClientCertificate(mode)` is for hosts where TLS terminates directly at Kestrel: it wires `KestrelServerOptions.ConfigureHttpsDefaults` and needs no separate middleware registration. `AddMtlsForwardedHeaderCertificate(configure)` is for hosts where TLS terminates at an ingress/gateway that forwards the client certificate as a request header instead — it registers `MtlsForwardedHeaderOptions` (with `HeaderName` **required**, no platform default, since nginx-ingress/Envoy/Istio/HAProxy each use a different header name/encoding) and must be paired with an explicit `app.UseMiddleware<MtlsForwardedHeaderMiddleware>()` call, mirroring `AddSharedKernelMultiTenancy()`'s "register services here, wire the middleware separately" split. Neither surface reimplements X.509 chain/revocation validation — both delegate the accept/reject decision to `12.Security`'s `SharedKernel.Security.Mtls.IMtlsCertificateValidator`, which must already be registered (typically via `AddMtlsAuthentication<TValidator>()`); omitting it throws at the first TLS handshake (Kestrel path) or first request (forwarded-header path), not at startup. Because Kestrel's `ClientCertificateValidation` delegate is synchronous but `IMtlsCertificateValidator.ValidateAsync` is async-only, `AddMtlsClientCertificate` bridges the two with a blocking `.GetAwaiter().GetResult()` call inside the TLS handshake — a real latency/thread-pool-starvation cost under load, so a validator used on this path must resolve quickly (an in-memory allow-list or a cached trust decision) and must never make a slow remote call (CRL/OCSP, an external policy service). A host that calls neither method is byte-identical in behavior to today — Kestrel's default `ClientCertificateMode.NoCertificate` stays untouched and no new middleware enters the pipeline.

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
