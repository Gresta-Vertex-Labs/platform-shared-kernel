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
app.UseExceptionHandler();
// app.UseMiddleware<MtlsForwardedHeaderMiddleware>();   // with AddMtlsForwardedHeaderCertificate()
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();   // with AddSharedKernelMultiTenancy(); after UseAuthentication()
app.UseRateLimiter();                              // with AddSharedKernelRateLimiting()
app.UseAuthorization();

// requireAuthorization: true adds .RequireAuthorization() to both endpoint mappings — DEFENSE IN DEPTH
// ONLY, NEVER A SUBSTITUTE FOR NETWORK ISOLATION. See "Health endpoint exposure" below.
app.MapDefaultHealthCheckEndpoints();              // /health/live, /health/ready
app.Run();
```

`samples/OrderApi/OrderApi.Api/Program.cs` is the compiled reference for this order.

A service that also uses `14.Presentation`'s `SharedKernel.Presentation.WebApi` (`builder.AddSharedKernelWebApi()`)
does not call `UseExceptionHandler()`, `UseAuthentication()`, `UseRateLimiter()` or `UseAuthorization()` itself:
`app.UseSharedKernelWebApi()` adds them in a fixed order and takes the middleware above in its hooks.
`UseSharedKernelRequestContext()` still runs first:

```csharp
app.UseSharedKernelRequestContext();                     // FIRST: correlation id + the request's context scope
app.UseSharedKernelWebApi(pipeline => pipeline
    .AtStart(a =>
    {
        a.UseMiddleware<MtlsForwardedHeaderMiddleware>(); // [pkg .Security.Mtls] before UseForwardedHeaders() and authentication
        a.UseForwardedHeaders();                          // when behind a proxy
    })
    .BeforeAuthorization(a =>
    {
        a.UseMiddleware<TenantResolutionMiddleware>();    // after authentication
        a.UseRequestLocalization();                       // [pkg .Localization] so 401, 403 and 429 answers are translated
    }));
app.MapEndpoints();                                      // the generated map of the service's IEndpointModules
```

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
5. **`app.UseRateLimiter()`** is required when `AddSharedKernelRateLimiting()` is used — after authentication, so a
   policy can partition by the caller — unless the service uses `UseSharedKernelWebApi()`, which adds it itself.
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
token routes. The `configure` delegate runs last and can override either threshold or add named policies:

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

Without `14.Presentation`, add the middleware yourself:

```csharp
builder.AddSharedKernelRateLimiting();
// ...
app.UseRateLimiter();
app.MapPost("/auth/token", TokenEndpoint).RequireRateLimiting(RateLimitPolicyNames.Authentication);
```

### Rejection body

`AddSharedKernelRateLimiting()` leaves `RateLimiterOptions.OnRejected` unset (P-562). On its own that gives ASP.NET
Core's default rejection: a bare `429` with no body. A service that also uses `14.Presentation`'s
`SharedKernel.Presentation.WebApi` gets the platform's RFC 9457 body with nothing to write — `AddSharedKernelWebApi()`
fills `OnRejected` whenever nothing else has — and `UseSharedKernelWebApi()` adds `UseRateLimiter()` itself: after
authentication, so a policy can partition by the caller, and before authorization, so requests refused with 401 or 403
still count against the limit:

```csharp
builder.AddSharedKernelWebApi();                       // 14.Presentation
builder.AddSharedKernelRateLimiting();

var app = builder.Build();
app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi();                           // includes UseRateLimiter()

app.MapPost("/auth/token", TokenEndpoint)
    .RequireRateLimiting(RateLimitPolicyNames.Authentication);
```

A rejected request is then answered `429 Too Many Requests` with `Content-Type: application/problem+json`, `errorCode`
`rate_limit.exceeded`, the same `traceId`/`correlationId` members as every other error response, and a `Retry-After`
header in whole seconds whenever the limiter reports a delay — the fixed-window limiters this method installs always
do. Never write a `ProblemDetails` body yourself to get this shape. A service that needs a different rejection sets its
own `OnRejected` in `configure`; `AddSharedKernelWebApi()` never overwrites a handler a service wrote.

`RateLimitRejectionRecipeTests` (`SharedKernel.ServiceDefaults.Tests`) proves this against real hosts through a
**test-only** reference to `SharedKernel.Presentation.WebApi`; `SharedKernel.ServiceDefaults` itself takes no
reference to `14.Presentation` in either direction.

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
`BaggageLogRecordProcessor` copies the platform's two `Activity` baggage items onto each record. That is how the
correlation id and the tenant id reach every log line without a call site passing them:

| Log attribute | Written by |
| --- | --- |
| `correlation.id` | `UseSharedKernelRequestContext()` (and `SharedKernel.Presentation.Grpc`'s correlation interceptor): the caller's `X-Correlation-Id` when it is valid, otherwise a new id |
| `TenantId` | `SharedKernel.MultiTenancy`'s `TenantResolutionMiddleware`, when a tenant resolves |

**Nothing else is copied (P-562 X2).** Baggage also comes from outside: a caller's W3C `baggage` header, and message
headers, which MassTransit copies onto the consuming activity. Copying every item would let an anonymous caller put any
property — a forged `SubjectId`, another tenant's `TenantId` — on every log record of its request. Both writers
*replace* their key, and a value containing a control character (CR, LF and the rest) or a Unicode line separator is
never copied. Any other value you want on a log record belongs in the log statement itself.

**A caller's baggage never reaches OpenTelemetry's baggage store either.** OpenTelemetry's ASP.NET Core
instrumentation used to read the request's `baggage` header into `Baggage.Current`, and the HttpClient and gRPC client
instrumentations then sent it to every downstream service. `AddSharedKernelTelemetry` decorates the default propagator
so a request's baggage is dropped there; trace context is still read, and baggage your service sets itself still
leaves with outgoing calls. The request's `Activity` is the other store: `UseSharedKernelRequestContext()` clears the
caller's items there at the edge (`TrustInboundBaggage`, off by default). A service serving HTTP without it keeps the
framework default, so a caller's `TenantId` or `correlation.id` item stays on the activity unless the middleware above
replaces it.

**Messages:** a consumer's log records carry the publisher's `correlation.id` and `TenantId`, which MassTransit carries
across in its own header, and nothing else from that header. The tenant a consumer acts on comes from dedicated message
headers (`07.Messaging`'s `WithInboundRequestContext()`), never from baggage.

See [`13.ServiceDefaults/CLAUDE.md`](CLAUDE.md) for the contracts and implementation rules.
