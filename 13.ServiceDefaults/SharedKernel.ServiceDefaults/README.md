# SharedKernel.ServiceDefaults

Host composition layer for Platform.SharedKernel microservices. One call wires OpenTelemetry, health checks, and the platform's startup conventions; everything beyond that is opt-in.

This is the composition root — the only package permitted to reference concrete providers from lower layers in order to assemble them.

## Included

### Entry point

**`AddServiceDefaults()`** — OpenTelemetry (traces, metrics, logs with ambient TenantId/CorrelationId enrichment) plus base health-check wiring.

**`MapDefaultHealthCheckEndpoints()`** — maps the liveness and readiness endpoints. Takes a `requireAuthorization` parameter; see the security note below.

### Readiness probes — opt in per dependency

Each wraps the probe primitive owned by that capability domain. `13.ServiceDefaults` supplies the `IHealthCheck` wiring; the domains supply the probes.

| Method | Probes |
|---|---|
| `AddDatabaseReadinessCheck<TContext>()` | EF Core connectivity |
| `AddDapperDatabaseReadinessCheck()` | `IDbConnectionFactory` connectivity |
| `AddCacheReadinessCheck()` / `AddRedisHealthCheck()` | Cache / Redis |
| `AddMessagingReadinessCheck()` | Message bus, against the real configured bus |
| `AddStorageReadinessCheck()` | Object storage |
| `AddSearchReadinessCheck()` | Search index |
| `AddVectorStoreReadinessCheck()` | Vector collection |
| `AddWorkflowReadinessCheck()` | Temporal workflow service |
| `AddSchedulerReadinessCheck()` | `19.Scheduling`'s hosted scheduling loop (in-process, zero I/O) |
| `AddKeyVaultKeyProviderReadinessCheck()` | The registered `IEncryptionKeyProvider`'s backing KMS/HSM (e.g. Azure Key Vault); requires `AddSharedKernelKeyVaultKeyProvider()` to have been called first |

### Telemetry activation — opt in per domain

`WithApplicationTelemetry` · `WithCachingTelemetry` · `WithCommunicationTelemetry` · `WithIntegrationTelemetry` · `WithIntelligenceTelemetry` · `WithMessagingTelemetry` · `WithPersistenceTelemetry` · `WithSchedulingTelemetry` · `WithSearchTelemetry` · `WithWorkflowTelemetry`

Each registers that domain's `ActivitySource` and/or `Meter` with the host providers.

### Other opt-ins

| Method | Purpose |
|---|---|
| `AddSharedKernelRateLimiting()` | BCL `Microsoft.AspNetCore.RateLimiting` with conservative defaults |
| `AddSharedKernelKeyVaultConfiguration()` | Azure Key Vault as an `IConfiguration` **source** |
| `AddSharedKernelKeyVaultKeyProvider()` | Azure Key Vault Keys as the `IEncryptionKeyProvider`/`IEnvelopeEncryptionProvider` **key provider** — distinct from the row above; see below |
| `AddMtlsClientCertificate()` | Kestrel client-certificate negotiation |
| `AddMtlsForwardedHeaderCertificate()` | Forwarded mTLS certificate header, restricted to trusted networks |
| `AddSharedKernelLocalization()` | Precedence-ordered request-culture resolution; see below |

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults()
       .WithPersistenceTelemetry()
       .WithMessagingTelemetry();

builder.Services.AddSharedKernelHealthChecks()
       .AddDatabaseReadinessCheck<AppDbContext>()
       .AddMessagingReadinessCheck();

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();
app.Run();
```

## Security note — health endpoints

`MapDefaultHealthCheckEndpoints()` maps unauthenticated endpoints by default, because Kubernetes probes cannot present credentials. Readiness output can disclose dependency topology.

Pass `requireAuthorization: true`, or keep these endpoints off your public ingress and restrict them with a `NetworkPolicy`. Do not expose readiness publicly on an internet-facing service.

## Key Vault: two independent, easily-confused methods

`AddSharedKernelKeyVaultConfiguration()` and `AddSharedKernelKeyVaultKeyProvider()` both talk to Azure Key Vault, but for entirely different reasons — a service may use either, both, or neither:

```csharp
// Wires Key Vault SECRETS as an additional IConfiguration source.
builder.AddSharedKernelKeyVaultConfiguration(new Uri("https://my-vault.vault.azure.net/"));

// Registers Key Vault KEYS as the platform's IEncryptionKeyProvider/IEnvelopeEncryptionProvider
// (e.g. for 06.Persistence's EncryptedValueConverter, 02.Caching's cache-value encryption).
builder.AddSharedKernelKeyVaultKeyProvider();
```

`AddSharedKernelKeyVaultKeyProvider()` is a thin call-through to `01.Core`'s `SharedKernel.Cryptography.KeyVault.Azure` — configure `AzureKeyVaultCryptographyOptions` under the `SharedKernel:Cryptography:KeyVault:Azure` configuration section (see that package's own README for the full shape: `VaultUri`, `CurrentKeyId`, `KeyNames`). It is idempotent — calling it more than once registers the provider exactly once.

By default (or when `cacheTtl` is left `null`), it also wraps `IEncryptionKeyProvider` — and *only* `IEncryptionKeyProvider` — in `01.Core`'s bounded-TTL `CachedEncryptionKeyProvider` (a 5-minute internal default). `IEnvelopeEncryptionProvider` and `IEncryptionKeyProviderProbe` always stay wired to the RAW, uncached provider — envelope wrap/unwrap is a real per-call vault operation, not a cacheable lookup, and a readiness probe must always observe live KMS state:

```csharp
// Default: IEncryptionKeyProvider is cache-wrapped with a 5-minute TTL.
builder.AddSharedKernelKeyVaultKeyProvider();

// A custom TTL.
builder.AddSharedKernelKeyVaultKeyProvider(cacheTtl: TimeSpan.FromMinutes(10));

// Explicit opt-out — IEncryptionKeyProvider resolves the raw, uncached provider, exactly as
// before this parameter existed.
builder.AddSharedKernelKeyVaultKeyProvider(cacheTtl: TimeSpan.Zero);
```

The cache-wrapped `CachedEncryptionKeyProvider` never unlocks a synchronous path — it never implements `01.Core`'s `ISynchronousEncryptionKeyProvider` marker, so the sync `Encrypt`/`Decrypt`/`EncryptToString`/`DecryptToString` members still throw `NotSupportedException` against it. The value here is strictly for async consumers.

`CachedEncryptionKeyProvider` is also independently resolvable as its own concrete type, so a service using `06.Persistence`'s encryption builder can target either the cached or the raw variant explicitly:

```csharp
efCorePersistenceBuilder.WithExternalEncryptionKeyProvider<CachedEncryptionKeyProvider>();
// — or —
efCorePersistenceBuilder.WithExternalEncryptionKeyProvider<AzureKeyVaultEncryptionKeyProvider>();
```

**This wiring is deliberately never automatic.** Calling `AddSharedKernelKeyVaultKeyProvider()` does NOT, by itself, make a service satisfy `06.Persistence`'s startup encryption-key-provider check — `06.Persistence` requires an explicit `.WithExternalEncryptionKeyProvider<TProvider>()` call on its own builder chain precisely to avoid an accidental ambient-registration-order collision between that package's `.WithEncryption()` and this method, both of which would otherwise silently share one unkeyed `IEncryptionKeyProvider` slot. If a service wants BOTH KMS-backed general-purpose crypto (via this method) AND KMS-backed persistence-layer column encryption, it must call `.WithExternalEncryptionKeyProvider<TProvider>()` itself.

**CROSS-DOMAIN HAZARD.** `07.Messaging`'s payload-encryption serializer path is hard-synchronous, with no async overload — it can never work against a KMS-backed `IEncryptionKeyProvider`, cache-wrapped by this method or not. A SERVICE THAT ENABLES BOTH `AddSharedKernelKeyVaultKeyProvider()` AND `07.Messaging`'S `WithPayloadTransform()` AGAINST THE SAME AMBIENT `IEncryptionKeyProvider`/`ISymmetricEncryptionService` SLOT WILL BREAK UNCONDITIONALLY (`NotSupportedException` on every message) once `01.Core`'s synchronous-capability gate ships. Keep messaging payload encryption on an independently-configured, config-backed `IEncryptionKeyProvider` — never the ambient slot this method registers.

Pair it with a readiness check so an unreachable vault shows up on `/health/ready`:

```csharp
builder.AddSharedKernelKeyVaultKeyProvider();

builder.Services.AddSharedKernelHealthChecks()
    .AddKeyVaultKeyProviderReadinessCheck();
```

`AddKeyVaultKeyProviderReadinessCheck()` resolves `01.Core`'s `IEncryptionKeyProviderProbe` (already registered as a byproduct of `AddSharedKernelKeyVaultKeyProvider()`) and reports `Unhealthy` — never `Degraded` — when the backing KMS/HSM is unreachable; no fail-safe/graceful-degradation layer sits in front of raw key-provider connectivity. Unlike `AddWorkflowReadinessCheck`/`AddSchedulerReadinessCheck`, this needed no new layering grant — `01.Core` is already inside this domain's `01`–`12` composition-root range.

## Culture resolution (`AddSharedKernelLocalization`)

Precedence-ordered request-culture resolution, composed on top of ASP.NET Core's own `RequestLocalizationMiddleware` — never a reimplementation. Resolves *precedence* only; it does not translate anything (pair it with `01.Core`'s `SharedKernel.Localization`/`ILocalizationCatalog` for that).

```csharp
builder.AddServiceDefaults();
builder.Services.AddSharedKernelMultiTenancy();
builder.Services.AddScoped<ITenantCatalog>(sp => /* see SharedKernel.MultiTenancy's README */);

builder.AddSharedKernelLocalization(o => o.UserPreferenceClaimType = "preferred_culture");

var app = builder.Build();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>(); // populates the ambient tenant id
app.UseRequestLocalization(); // the real BCL call — this package never wires it for you
```

Default resolution order — deliberately signed-signal-before-unsigned-header, mirroring the `SharedKernel.MultiTenancy` `StrategyOrder` security lesson:

1. **`UserPreference`** — an authenticated user's own stored preference claim (`IUserContext.Claims[UserPreferenceClaimType]`). Skipped cleanly when `UserPreferenceClaimType` is left unconfigured.
2. **`TenantDefault`** — the current tenant's `TenantDescriptor.DefaultCulture`, via an optionally-registered `ITenantCatalog`. Skipped cleanly (never throws) when no `ITenantCatalog` is registered.
3. **`AcceptLanguageHeader`** — the real BCL `AcceptLanguageHeaderRequestCultureProvider`.

A one-time startup warning fires when neither `UserPreferenceClaimType` nor an `ITenantCatalog` is configured — both dynamic steps are structurally dead, and you are almost certainly resolving culture from `Accept-Language` alone by accident rather than by design.

## Rate limiting and ProblemDetails

`AddSharedKernelRateLimiting()` leaves `OnRejected` at the BCL bare-429 default and takes **no** reference to `14.Presentation`. A service that wants an RFC 9457 body attaches its own handler via the `configure` parameter and calls `14.Presentation`'s `RateLimitRejectionProblemDetails.Create(...)`. That keeps the two packages independently referenceable.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [13.ServiceDefaults README](../README.md) for the full host-composition layer, including `SharedKernel.MultiTenancy`.
