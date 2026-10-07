using Microsoft.Extensions.Compliance.Redaction;
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline;
using SharedKernel.Compression.Extensions;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.KeyVault.Azure;
using SharedKernel.DataPrivacy.DataSubjectRequests;
using SharedKernel.DataPrivacy.Redaction;
using SharedKernel.Integration.Webhooks.Extensions;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.MultiTenancy.Extensions;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Persistence;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.ApiKey.Extensions;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.ServiceDefaults.Telemetry;
using SharedKernel.Validation;
using SharedKernel.Validation.FluentValidation;
using Shop.Billing.Api.Payments;
using Shop.Billing.Api.Persistence;
using Shop.Billing.Api.Privacy;
using Shop.Billing.Api.Security;

var builder = WebApplication.CreateBuilder(args);

// Key Vault first: its secrets (the API key hashes, the merchants' webhook secrets) become configuration.
builder.AddBillingKeyVault();

// Registered first, so the vault has a data key before the migrations and the first request need one.
builder.Services.AddHostedService<DataKeyProvisioning>();

builder.AddServiceDefaults();
builder.WithApplicationTelemetry();
builder.WithPersistenceTelemetry();

// 01.Core DataPrivacy: values marked [EmailAddressData], [BankAccountData]… are redacted in every log.
builder.Services.AddRedaction(redaction => redaction.SetPrivacyRedactors());
builder.Logging.EnableRedaction(options => options.ApplyDiscriminator = false);

// Two kinds of caller on the same endpoints: merchants with a Keycloak token, and API-key clients (the Ordering
// service, the payment provider). The X-Api-Key header decides which scheme authenticates the request.
builder.Services.AddOidcAuthentication(builder.Configuration);
builder.Services.AddManagedApiKeyAuthentication<ConfigurationApiKeyStore>(keys =>
    keys.Prefix = "shop_dev"
);
builder.Services.AddAuthorization();
builder.Services.AddSharedKernelRequestContext();
builder.Services.AddSharedKernelMultiTenancy(o =>
    builder.Configuration.GetSection(TenantResolutionOptions.SectionName).Bind(o)
);
builder.Services.AddScoped<ITenantResolutionStrategy, ServiceHeaderTenantResolutionStrategy>();

builder.AddSharedKernelPostgres<BillingDbContext>(
    BillingDatabase.ConnectionName,
    p => BillingDatabase.Configure(p).UseServiceName("billing-api").MigrateOnStartup()
);

// 01.Core: invoice signing inside Key Vault, the IBAN envelope-encrypted under a Key Vault master key, compression.
builder
    .Services.AddSharedKernelCryptography(builder.Configuration)
    .AddAzureKeyVaultSigning(builder.Configuration)
    .AddAzureKeyVaultEncryption(builder.Configuration)
    .AddAsymmetricSigning()
    .AddEnvelopeEncryption();
builder.Services.AddSharedKernelCompression(builder.Configuration);
builder.Services.AddSharedKernelValidation();
builder.Services.AddValidatedOptions<BillingOptions>(builder.Configuration);
builder.Services.AddClock();
builder.Services.AddScoped<InvoiceIssuer>();
builder.Services.AddScoped<PaymentAnnouncements>();
builder.Services.AddScoped<IDataSubjectRequestHandler, BillingDataSubjectHandler>();

// 15.Integration: signed webhooks to the merchant (on localhost in development, hence private targets allowed).
builder.Services.AddSharedKernelWebhooks();
builder.Services.AddScoped<IWebhookSubscriptionStore, ConfigurationWebhookSubscriptionStore>();

// 07.Messaging over Azure Service Bus: the dispatcher publishes WebhookDeliveryExhausted when a merchant stays down.
builder
    .Services.AddSharedKernelMessaging(builder.Configuration)
    .UseAzureServiceBus(
        builder.Configuration.GetConnectionString(BillingMessaging.ConnectionName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{BillingMessaging.ConnectionName} is not configured."
            )
    )
    .WithRetry()
    .Build();

builder.Services.AddFluentValidationRequestValidators(typeof(Program).Assembly);
builder.Services.AddSharedKernelApplication(
    typeof(Program).Assembly,
    app => app.UseMediatR().WithTransactions()
);

builder
    .Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<BillingDbContext>()
    .AddSharedKernelReadiness();

builder.AddSharedKernelWebApi();

var app = builder.Build();

app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi(pipeline =>
    pipeline.BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>())
);

app.MapDefaultHealthCheckEndpoints();
app.MapEndpoints();

app.Lifetime.ApplicationStarted.Register(() =>
    app.Services.GetRequiredService<StartupGate>().MarkReady()
);

await app.RunAsync();

/// <summary>Exposed for <c>WebApplicationFactory</c>.</summary>
public partial class Program;

/// <summary>Billing's bus connection (<c>ConnectionStrings:servicebus</c>, set by the AppHost).</summary>
internal static class BillingMessaging
{
    public const string ConnectionName = "servicebus";
}
