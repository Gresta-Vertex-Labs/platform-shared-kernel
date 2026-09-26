# 13.ServiceDefaults

Host composition for Platform.SharedKernel microservices. Every package here is **Host tier**: a service's API or
worker project references it; nothing below the host does.

| Package | Provides |
| --- | --- |
| [`SharedKernel.ServiceDefaults`](SharedKernel.ServiceDefaults/README.md) | The composition base: `AddServiceDefaults()` (OpenTelemetry traces, metrics and logs; base health checks), `AddSharedKernelReadiness()`, `MapDefaultHealthCheckEndpoints()`, `StartupGate`, every `WithXTelemetry()`, `AddSharedKernelRateLimiting()`. References only `SharedKernel.Primitives` and OpenTelemetry. |
| [`SharedKernel.ServiceDefaults.Security`](SharedKernel.ServiceDefaults.Security/README.md) | `AddSharedKernelRequestContext()` + `app.UseSharedKernelRequestContext()` — the caller, tenant and correlation id every layer reads through `IRequestContext` |
| [`SharedKernel.ServiceDefaults.Persistence`](SharedKernel.ServiceDefaults.Persistence/README.md) | `AddDatabaseReadinessCheck<TContext>()`, `AddDapperDatabaseReadinessCheck()`, `AddPersistenceStartupReadinessCheck()` |
| [`SharedKernel.MultiTenancy`](SharedKernel.MultiTenancy/README.md) | Tenant resolution (claim, header, tenant directory), `TenantResolutionMiddleware`, the tenant catalog |
| [`SharedKernel.ServiceDefaults.Security.Mtls`](SharedKernel.ServiceDefaults.Security.Mtls/README.md) | `AddMtlsClientCertificate()`, `AddMtlsForwardedHeaderCertificate()`, `MtlsForwardedHeaderMiddleware` |
| [`SharedKernel.ServiceDefaults.Configuration.KeyVault`](SharedKernel.ServiceDefaults.Configuration.KeyVault/README.md) | `AddSharedKernelKeyVaultConfiguration(vaultUri)` — Key Vault secrets as configuration |
| [`SharedKernel.ServiceDefaults.Localization`](SharedKernel.ServiceDefaults.Localization/README.md) | `AddSharedKernelLocalization()` — per-request culture resolution |

Readiness needs no per-dependency package. Each provider registers its own `IReadinessProbe` when you register the
provider (`AddRedisConnection`, `MessagingBusBuilder.Build()`, each storage store, search index and vector
collection, the Temporal worker, the scheduler, field encryption, the audit sealer, the Key Vault key provider), and
one `AddSharedKernelReadiness()` call maps every probe the service actually has to `/health/ready`.

## Program.cs composition

```csharp
var builder = WebApplication.CreateBuilder(args);

// Optional, [SharedKernel.ServiceDefaults.Configuration.KeyVault]: vault secrets available to every later call.
builder.AddSharedKernelKeyVaultConfiguration(new Uri("https://my-vault.vault.azure.net/"));

builder.AddServiceDefaults();                                    // FIRST: OpenTelemetry + base health checks
builder.Services.AddOidcAuthentication(builder.Configuration);    // 12.Security: registers IUserContext
builder.Services.AddSharedKernelRequestContext();                // [.Security] IRequestContext over IUserContext
builder.Services.AddSharedKernelMultiTenancy();                  // optional: multi-tenant services
builder.AddSharedKernelRateLimiting();                           // optional

builder.Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<OrdersDbContext>()                // [.Persistence]
    .AddSharedKernelReadiness();                                 // every provider's readiness probe

builder.WithMessagingTelemetry()                                 // only the domains this service uses
       .WithPersistenceTelemetry()
       .WithApplicationTelemetry();

// Optional, [.Security.Mtls]: pick the one matching where TLS terminates.
builder.AddMtlsClientCertificate(ClientCertificateMode.RequireCertificate);   // at Kestrel
// builder.AddMtlsForwardedHeaderCertificate(o =>                              // at an ingress
// {
//     o.HeaderName = "ssl-client-cert";
//     o.AddTrustedNetwork(IPNetwork.Parse("10.0.0.0/16"));
// });

var app = builder.Build();

app.UseSharedKernelRequestContext();               // FIRST: correlation id + the request's context scope
app.UseSharedKernelSecurityHeaders();              // 14.Presentation.WebApi
app.UseExceptionHandler();
// app.UseMiddleware<MtlsForwardedHeaderMiddleware>();   // with AddMtlsForwardedHeaderCertificate()
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();   // with AddSharedKernelMultiTenancy(); after UseAuthentication()
app.UseAuthorization();
app.UseRateLimiter();                              // with AddSharedKernelRateLimiting()

app.MapDefaultHealthCheckEndpoints();              // /health/live, /health/ready
app.Run();
```

`samples/OrderApi/OrderApi.Api/Program.cs` is the compiled reference for this order.

### Ordering rules

1. **`builder.AddServiceDefaults()` is the first call.** It wires OpenTelemetry and the always-on `"startup"` check.
   Chain your readiness checks onto `builder.Services.AddHealthChecks()`; a second `AddSharedKernelHealthChecks()`
   registers `"startup"` twice and the host throws at startup.
2. **`app.UseSharedKernelRequestContext()` is the first middleware**, before `UseExceptionHandler()`, so every later
   middleware logs with the correlation id and every response — error responses included — echoes
   `X-Correlation-Id`. It is safe before `UseAuthentication()`: the caller is read only when something first asks.
3. **`TenantResolutionMiddleware` runs after `UseAuthentication()`** — the claim strategy needs the authenticated
   user. It opens an inner request-context scope that replaces only the tenant. Registering
   `AddSharedKernelMultiTenancy()` without the middleware leaves the tenant as the one the caller's credential
   asserts, with no header or directory resolution.
4. **`MtlsForwardedHeaderMiddleware`** must be added explicitly when `AddMtlsForwardedHeaderCertificate()` is used,
   and before `UseAuthentication()` so the certificate is set when authentication runs.
5. **`app.UseRateLimiter()`** is required when `AddSharedKernelRateLimiting()` is used.
6. **Registration order of readiness probes does not matter**: `AddSharedKernelReadiness()` reads the probes when
   health checks are first resolved.

## Readiness

| Probe name | Registered by |
| --- | --- |
| `messaging` | `MessagingBusBuilder.Build()` (`07.Messaging`) |
| `redis` | `AddRedisConnection(configuration)` (`02.Caching`) |
| `cache` | the FusionCache registration (`02.Caching`) |
| `encryption-key-provider` | the Azure Key Vault encryption key provider (`01.Core`) |
| `field-encryption` | `UseFieldEncryption()` (`06.Persistence`) |
| `audit-sealing` | `UseAuditTrail()` (`06.Persistence`) |
| `storage-{store}` | each `AddStore(name)` / `AddTenantStore(name)` (`08.Storage`) |
| `search-{provider}-{index}` | each registered search index (`09.Search`) |
| `vector-store-{provider}-{collection}` | each registered vector collection (`10.Intelligence`) |
| `workflows` | the Temporal worker registration (`17.Workflows`) |
| `scheduler` | `AddSharedKernelScheduling()` (`19.Scheduling`) |

Leave one off `/health/ready` with `AddSharedKernelReadiness(o => o.Exclude("cache"))`, or give every probe a time
limit with `o.Timeout`. A service's own dependency implements `IReadinessProbe` and registers it with
`services.AddReadinessProbe<T>()` (`SharedKernel.Primitives.Health`).

## Health endpoint exposure

**`/health/live` AND `/health/ready` MUST BE NETWORK-RESTRICTED AT THE INGRESS/`NETWORKPOLICY` LAYER IN ANY
ENVIRONMENT WHERE THEY ARE NOT INTENTIONALLY PUBLIC.** `MapDefaultHealthCheckEndpoints(requireAuthorization: true)` is
defense in depth only — a kubelet's probes are unauthenticated, so enabling it on the endpoints the kubelet calls
fails those probes. When health data reaches an external audience, serve only the overall status:

```csharp
app.MapHealthChecks("/health/status", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync($$"""{"status":"{{report.Status}}"}""");
    },
});
```

## Rate limiting

`AddSharedKernelRateLimiting()` wraps ASP.NET Core's `Microsoft.AspNetCore.RateLimiting`: a global fixed window per
remote IP (100 requests/minute) and the named `RateLimitPolicyNames.Authentication` policy (10/minute) for login and
token routes. The `configure` delegate runs last and can override either.

```csharp
builder.AddSharedKernelRateLimiting();
// ...
app.UseRateLimiter();
app.MapPost("/auth/token", TokenEndpoint).RequireRateLimiting(RateLimitPolicyNames.Authentication);
```

Rejections are the BCL's bare 429 unless you set `OnRejected`. For an RFC 9457 body, call
`14.Presentation.WebApi`'s `RateLimitRejectionProblemDetails.Create` — the only sanctioned way to shape it; it also
sets the `Retry-After` header:

```csharp
using System.Threading.RateLimiting;
using SharedKernel.Presentation.WebApi.RateLimiting;

builder.AddSharedKernelRateLimiting(options =>
{
    options.OnRejected = async (context, ct) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? delay : (TimeSpan?)null;
        var problem = RateLimitRejectionProblemDetails.Create(context.HttpContext, retryAfter);
        await context.HttpContext.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
    };
});
```

`SharedKernel.ServiceDefaults` takes no reference to `14.Presentation`; the recipe lives in the service.

## Tenant status validation

`ITenantStatusValidator` lets `TenantResolutionMiddleware` reject a resolved tenant that is suspended or offboarded.
It is optional; when registered, a `false` result fails closed exactly like "no tenant resolved":

```csharp
public sealed class SqlTenantStatusValidator(ITenantDirectory directory) : ITenantStatusValidator
{
    public async Task<bool> IsActiveAsync(TenantId tenantId, CancellationToken ct) =>
        await directory.GetStatusAsync(tenantId, ct) is TenantStatus.Active;
}

builder.Services.AddScoped<ITenantStatusValidator, SqlTenantStatusValidator>();
```

`CatalogTenantStatusValidator` is a ready-made implementation over `ITenantCatalog` — see the
[MultiTenancy README](SharedKernel.MultiTenancy/README.md).

## Log enrichment

`AddServiceDefaults()` exports every log record through OTLP with scopes and formatted messages, and
`BaggageLogRecordProcessor` copies every `Activity` baggage entry onto each record. That is how the correlation id
(set by `UseSharedKernelRequestContext()` and `SharedKernel.Presentation.Grpc`'s correlation interceptor) and the
tenant id (set by `TenantResolutionMiddleware` when a tenant resolves) reach every log line without a call site
passing them. The processor names no key; any baggage a component sets is enriched the same way.

See [`13.ServiceDefaults/CLAUDE.md`](CLAUDE.md) for the contracts and implementation rules.
