using SharedKernel.Configuration.Extensions;
using SharedKernel.Integration.Notifications.Abstractions.Extensions;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Email.SendGrid.Extensions;
using SharedKernel.Integration.Notifications.Sms.Twilio.Extensions;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;
using SharedKernel.ServiceDefaults.Telemetry;
using Shop.Notify.Worker;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.WithMessagingTelemetry();
builder.Services.AddValidatedOptions<NotifyOptions>(builder.Configuration);

// 15.Integration: email through SendGrid, SMS through Twilio. Neither package binds configuration, so the host reads
// its own keys; BaseAddress points both at the platform's stand-in (WireMock) in development.
var providers = builder.Configuration.GetSection(NotifyProviders.Section);
builder.Services.AddSharedKernelNotifications();
builder.Services.AddScoped<INotificationSenderIdentityResolver, ShopSenderIdentity>();
builder.Services.AddSendGridEmailNotifications(o =>
{
    o.ApiKey = providers[NotifyProviders.SendGridApiKey]!;
    o.BaseAddress = new Uri(providers[NotifyProviders.SendGridBaseAddress]!);
});
builder.Services.AddTwilioSmsNotifications(o =>
{
    o.AccountSid = providers[NotifyProviders.TwilioAccountSid]!;
    o.AuthToken = providers[NotifyProviders.TwilioAuthToken]!;
    o.From = providers[NotifyProviders.TwilioFrom];
    o.BaseAddress = new Uri(providers[NotifyProviders.TwilioBaseAddress]!);
});

// 07.Messaging: Billing's receipts from RabbitMQ, with the publisher's tenant as the consumer's caller.
builder
    .Services.AddSharedKernelMessaging(builder.Configuration)
    .UseRabbitMq(
        builder.Configuration.GetConnectionString(NotifyProviders.RabbitMq)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{NotifyProviders.RabbitMq} is not configured."
            )
    )
    .WithRetry()
    .WithInboundRequestContext()
    .AddConsumer<ReceiptDueConsumer>()
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();
app.Lifetime.ApplicationStarted.Register(() =>
    app.Services.GetRequiredService<StartupGate>().MarkReady()
);

await app.RunAsync();

/// <summary>Where Notify finds its providers (<c>Notify:Providers</c>) and its bus.</summary>
internal static class NotifyProviders
{
    public const string Section = "Notify:Providers";
    public const string SendGridApiKey = "SendGrid:ApiKey";
    public const string SendGridBaseAddress = "SendGrid:BaseAddress";
    public const string TwilioAccountSid = "Twilio:AccountSid";
    public const string TwilioAuthToken = "Twilio:AuthToken";
    public const string TwilioFrom = "Twilio:From";
    public const string TwilioBaseAddress = "Twilio:BaseAddress";
    public const string RabbitMq = "rabbitmq";
}

/// <summary>Exposed for tests.</summary>
public partial class Program;
