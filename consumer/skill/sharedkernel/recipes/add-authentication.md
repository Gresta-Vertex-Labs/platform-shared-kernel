---
proven-by:
  - samples/README.md
  - samples/Shop/Ordering/Shop.Ordering.Api/Program.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/OrderEndpoints.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/TotpStores.cs
  - samples/Shop/Ordering/Shop.Ordering.Api/appsettings.json
  - samples/Shop/Ordering/Shop.Ordering.Application/OrderCommands.cs
  - samples/Shop/Ordering/Shop.Ordering.Application/OrderingPorts.cs
  - samples/Shop/Ordering/Shop.Ordering.Application/TotpCommands.cs
  - samples/Shop/Ordering/Shop.Ordering.Infrastructure/OrderingInfrastructure.cs
  - samples/Shop/Billing/Shop.Billing.Api/Program.cs
  - samples/Shop/Billing/Shop.Billing.Api/BillingEndpoints.cs
  - samples/Shop/Billing/Shop.Billing.Api/Security/BillingSecurity.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Program.cs
  - samples/Shop/Inventory/Shop.Inventory.Api/Security/InventorySecurity.cs
unproven:
  - Hashing and verifying a password with Argon2id: the reference platform registers it but stores no passwords
  - RequireRole and RequireFreshAuthentication attributes: no Shop endpoint gates on a role or on sign-in age alone
---
# Add authentication and authorization

**Use when:** deciding who the caller is (OIDC bearer tokens, API keys, client certificates), gating use cases
by permission, gating endpoints at the edge, or demanding an authenticator-app step-up. **Not when:** resolving
the caller's tenant (add-multi-tenancy.md) or sending credentials on outbound calls (add-outbound-call.md).

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Application | `SharedKernel.Application` | `[RequirePermission]`, enforced by the pipeline on every path |
| Infrastructure | `SharedKernel.Cryptography`, `SharedKernel.Cryptography.Argon2` | `AddArgon2id(…)` for password hashing; TOTP verification state |
| Api | `SharedKernel.ServiceDefaults.Security` | `AddSharedKernelRequestContext()`: `IUserContext` becomes the one `IRequestContext` |
| Api | `SharedKernel.Security.Oidc` | JWT bearer tokens from any OpenID Connect provider |
| Api | `SharedKernel.Security.ApiKey` | API keys for machine clients, hashed at rest |
| Api | `SharedKernel.Security.Mtls`, `SharedKernel.ServiceDefaults.Security.Mtls` | client-certificate authentication and Kestrel wiring |
| Api | `SharedKernel.Security.Totp` | authenticator enrollment and session-bound step-up |
| Api | `SharedKernel.Presentation.Core` (via `SharedKernel.Presentation.WebApi`) | `[RequireEndpointPermission]`, `RequireAuthenticationMethod(…)` |
| Api | `SharedKernel.Security.Abstractions` | `IUserContext`, read by host code only |

Application code never reads claims or `IUserContext`: it injects `IRequestContext` (tenant, actor, permissions).

## Steps

1. **Application: declare permissions on the request.** Unauthenticated callers get 401, callers without the
   permission 403, on HTTP, consumers, workflow activities and jobs alike:

   ```csharp
   using SharedKernel.Application.Authorization;
   using SharedKernel.Application.Messaging;

   public static class OrderingPermissions
   {
       public const string Place = "orders.place";
       public const string Read = "orders.read";
       public const string Cancel = "orders.cancel";
   }

   [RequirePermission(OrderingPermissions.Cancel)]
   public sealed record CancelOrderCommand(Guid OrderId) : ICommand;
   ```

2. **Infrastructure: cryptography and Argon2id.** Register once and return the builder, so the host can add step-up:

   ```csharp
   using Microsoft.Extensions.Hosting;
   using SharedKernel.Cryptography.Argon2;
   using SharedKernel.Cryptography.Extensions;

   public static ICryptographyBuilder AddOrderingCryptography(this IHostApplicationBuilder builder) =>
       builder.Services.AddSharedKernelCryptography(builder.Configuration).AddArgon2id(builder.Configuration);
   ```

   How to hash and verify a password with it is in the `SharedKernel.Cryptography.Argon2` README; the reference
   platform does not store passwords.

3. **Api: OIDC and the request context.** Authentication first, then the request context; the request-context
   middleware goes before everything:

   ```csharp
   using SharedKernel.Security.Oidc.Extensions;
   using SharedKernel.ServiceDefaults.Security;

   builder.Services.AddOidcAuthentication(builder.Configuration);
   builder.Services.AddAuthorization();
   builder.Services.AddSharedKernelRequestContext();
   // …
   app.UseSharedKernelRequestContext();
   app.UseSharedKernelWebApi();   // authentication and authorization run inside it
   ```

4. **Api: API keys.** Implement `IApiKeyStore` (where the key hash, client and permissions live), then register it
   next to OIDC. The `X-Api-Key` header decides which scheme authenticates a request:

   ```csharp
   using SharedKernel.Execution.Tenancy;
   using SharedKernel.Security.ApiKey;
   using SharedKernel.Security.ApiKey.Extensions;
   using SharedKernel.Security.ApiKey.Keys;

   public sealed class ConfigurationApiKeyStore(IConfiguration configuration) : IApiKeyStore
   {
       public const string Section = "Billing:ApiKeys";
       public const string HashKey = "Hash";
       public const string ClientIdKey = "ClientId";
       public const string TenantIdKey = "TenantId";
       public const string PermissionsKey = "Permissions";

       public ValueTask<ApiKeyRecord?> FindAsync(string keyId, CancellationToken cancellationToken)
       {
           var client = configuration.GetSection($"{Section}:{keyId}");
           string? hash = client[HashKey];
           string? clientId = client[ClientIdKey];
           if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(clientId))
           {
               return ValueTask.FromResult<ApiKeyRecord?>(null);
           }
           return ValueTask.FromResult<ApiKeyRecord?>(
               new ApiKeyRecord(keyId, hash, clientId)
               {
                   TenantId = TenantId.TryParse(client[TenantIdKey], out var tenant) ? tenant : null,
                   Permissions = client.GetSection(PermissionsKey).Get<string[]>() ?? [],
               });
       }
   }

   builder.Services.AddManagedApiKeyAuthentication<ConfigurationApiKeyStore>(keys => keys.Prefix = "shop_dev");
   ```

5. **Api: client certificates (mTLS).** The framework checks the chain; your `IMtlsCertificateValidator` decides who
   the client is and what it may do:

   ```csharp
   using System.Security.Cryptography;
   using System.Security.Cryptography.X509Certificates;
   using SharedKernel.Security.Mtls;
   using SharedKernel.Security.Mtls.Validation;

   public sealed class ShopServiceCertificateValidator(IConfiguration configuration) : IMtlsCertificateValidator
   {
       public ValueTask<MtlsValidationResult> ValidateAsync(
           X509Certificate2 certificate, CancellationToken cancellationToken)
       {
           string thumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
           string? clientId = configuration[$"Inventory:Mtls:Clients:{thumbprint}"];
           return ValueTask.FromResult(clientId is null
               ? MtlsValidationResult.Failure("UnknownClient")
               : MtlsValidationResult.Success(clientId, roles: ["shop-service"],
                   permissions: [InventoryPermissions.Reserve]));
       }
   }
   ```

   ```csharp
   using System.Security.Cryptography.X509Certificates;
   using Microsoft.AspNetCore.Server.Kestrel.Https;
   using SharedKernel.Security.Mtls.Extensions;
   using SharedKernel.ServiceDefaults.Security;

   builder.Services.AddMtlsAuthentication<ShopServiceCertificateValidator>(mtls =>
   {
       mtls.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
       mtls.CustomTrustStore.AddRange(ShopCertificateAuthority.Load(builder.Configuration));
   });
   builder.AddMtlsClientCertificate(ClientCertificateMode.AllowCertificate);
   ```

   With both bearer tokens and certificates on one host, add an ASP.NET Core policy scheme whose
   `ForwardDefaultSelector` picks `MtlsAuthenticationDefaults.AuthenticationScheme` when a client certificate is
   present, and an authorization policy restricted to that scheme for the service-only endpoints.

6. **Api: TOTP step-up.** Implement `ITotpStepUpStore` (shared by every replica; Redis in the reference platform) and
   `IRecoveryCodeStore`, register them on the cryptography builder, then gate the endpoint:

   ```csharp
   using SharedKernel.Security.Totp;

   builder
       .AddOrderingCryptography()
       .AddTotpStepUp<RedisTotpStepUpStore, NoRecoveryCodeStore>(o => o.FreshnessWindow = TimeSpan.FromMinutes(5));
   ```

   ```csharp
   using SharedKernel.Application.Messaging;
   using SharedKernel.Presentation.Authorization;
   using SharedKernel.Presentation.WebApi;

   orders
       .MapPost("/{id:guid}/cancel", (Guid id, ISender sender, CancellationToken ct) =>
           sender.Send(new CancelOrderCommand(id), ct).ToNoContent())
       .RequireAuthenticationMethod(TimeSpan.FromMinutes(5), "otp");
   ```

   Enrollment uses `TotpEnrollmentService.Create(…)` and stores the secret through a command (encrypt the column,
   add-persistence.md); verification uses `TotpChallengeService.VerifyCodeAsync(user, secret, code, …)`, which
   records the step-up for the session. The Infrastructure project supplies `ITotpReplayGuard` and
   `ITotpAttemptThrottle`.

7. **Api: edge authorization without a command.** For an endpoint that sends no command, or a hub:

   ```csharp
   using SharedKernel.Application.Messaging;
   using SharedKernel.Presentation.Authorization;
   using SharedKernel.Presentation.WebApi;

   app.MapPost("/provider/disputes", (DisputeNotice body, ISender sender, CancellationToken ct) =>
           sender.Send(new RecordDisputeCommand(body.PaymentId), ct).ToOk())
       .RequireAuthorization()
       .RequireEndpointPermission(BillingPermissions.Callback);
   ```

   `[RequireEndpointPermission]` also goes on a SignalR hub class (add-http-endpoint.md). `[RequireRole]` and
   `[RequireFreshAuthentication]` exist in `SharedKernel.Presentation.Core` but are not exercised by the
   reference platform: read its README before using them.

8. **Configuration.** OIDC binds `SharedKernel:Security:Oidc` (audiences, permission claim types). Secrets such as
   API key hashes belong in a secret store (Key Vault as configuration, new-service.md). The keys are in each README.

## Test it

- Use cases: `TestRequestContext.ForTenant(tenant).WithPermissions(…)` through `ApplicationPipelineTestHarness`;
  a caller without the permission must get `ErrorType.Forbidden` and write nothing (write-tests.md).
- A certificate validator: build a certificate with `MtlsTestCertificateBuilder` from `SharedKernel.Security.Testing`
  and call `ValidateAsync` directly.
- Step-up and API-key stores: run them over the in-memory doubles (`SharedKernel.Security.Testing`,
  `SharedKernel.Caching.Redis.Testing`).

## Check

- `SK0031` flags injecting `IHttpContextAccessor`, `HttpContext` or `ClaimsPrincipal`: use `IRequestContext`
  (or `IUserContext` in host code).
- `SK0022` flags literal header names and claim types; use the named constants.
- `SK0040` flags `[RequirePermission]` on a request whose response is not `Result`-shaped.
- A use case without `[RequirePermission]` gets no permission check from the pipeline: only the endpoint's own
  authorization guards it, and nothing guards it on a consumer, workflow or job path.
- A missing tenant fails closed: return `Error.Forbidden`, never fall back to a default tenant.

## Read next

`SharedKernel.ServiceDefaults.Security`, `SharedKernel.Security.Oidc`, `SharedKernel.Security.ApiKey`,
`SharedKernel.Security.Mtls`, `SharedKernel.ServiceDefaults.Security.Mtls`, `SharedKernel.Security.Totp`,
`SharedKernel.Presentation.Core`, `SharedKernel.Cryptography.Argon2`.
