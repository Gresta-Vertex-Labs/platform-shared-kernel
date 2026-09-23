// consumer-verify — exercises the composed Program.cs shape documented in
// SharedKernel.Integration.Webhooks/README.md: AddSharedKernelWebhooks() plus a consumer-supplied
// IWebhookSubscriptionStore resolves IWebhookDispatcher with zero DI exceptions (Surface 1), and
// omitting IWebhookSubscriptionStore produces a clear, actionable DI resolution failure at first
// dispatch rather than a silent null or no-op (Surface 2).

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Contracts.Events;
using SharedKernel.Integration.Notifications.Abstractions.Extensions;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Email.SendGrid;
using SharedKernel.Integration.Notifications.Email.SendGrid.Extensions;
using SharedKernel.Integration.Notifications.Sms.Twilio;
using SharedKernel.Integration.Notifications.Sms.Twilio.Extensions;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Extensions;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

// ── Surface 1: AddSharedKernelWebhooks() + a registered IWebhookSubscriptionStore ──────────────
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    services.AddSharedKernelWebhooks();
    services.AddScoped<IWebhookSubscriptionStore, NoSubscriptionsStore>();

    // IWebhookDispatcher's constructor also requires IEventPublisher (07.Messaging.Abstractions) —
    // AddSharedKernelWebhooks deliberately does not register it (that wiring is 07.Messaging's
    // concern); the consuming service's own AddSharedKernelMessaging() call supplies it in
    // production. A no-op fake stands in for it here so this harness proves only what it claims:
    // the webhook package's own DI surface composes, not 07.Messaging's.
    services.AddScoped<IEventPublisher, NoOpEventPublisher>();

    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();

    var dispatcher = scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();
    Verify(dispatcher is WebhookDispatcher, "IWebhookDispatcher resolves as WebhookDispatcher");

    // Exercising DispatchAsync end-to-end (zero subscriptions registered) proves the full pipeline
    // — options, signature provider, named HttpClient, IWebhookSubscriptionStore, IEventPublisher —
    // composes with zero DI exceptions, not just that the dispatcher type itself resolves.
    var results = await dispatcher.DispatchAsync(
        new ConsumerVerifyIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow),
        CancellationToken.None);
    Verify(results.Count == 0, "DispatchAsync completes with zero matched subscriptions, zero DI exceptions");
}

Console.WriteLine("Surface 1 PASS: AddSharedKernelWebhooks() + registered IWebhookSubscriptionStore resolves IWebhookDispatcher with zero DI exceptions");

// ── Surface 2: omitting IWebhookSubscriptionStore — clear, actionable DI failure ────────────────
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    services.AddSharedKernelWebhooks();
    services.AddScoped<IEventPublisher, NoOpEventPublisher>();
    // Deliberately no IWebhookSubscriptionStore registration — WebhookDispatcher takes it as a
    // required constructor parameter, so resolution fails at first GetRequiredService<IWebhookDispatcher>(),
    // not deeper inside DispatchAsync. That is itself the proof: failure surfaces immediately and
    // unambiguously at first use, never as a silently-resolved dispatcher that no-ops at call time.

    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();

    InvalidOperationException? caught = null;
    try
    {
        scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();
    }
    catch (InvalidOperationException ex)
    {
        caught = ex;
    }

    Verify(caught is not null, "Resolving IWebhookDispatcher throws InvalidOperationException, not a silent null/no-op, when IWebhookSubscriptionStore is unregistered");
    Verify(
        caught!.Message.Contains(nameof(IWebhookSubscriptionStore), StringComparison.Ordinal),
        "The DI resolution failure message names IWebhookSubscriptionStore — actionable, not generic");

    Console.WriteLine($"  Captured failure message: \"{caught!.Message}\"");
}

Console.WriteLine("Surface 2 PASS: omitting IWebhookSubscriptionStore produces a clear, actionable DI resolution failure at first use");

// ── Surface 3 (WO-072): both Notifications providers registered together — unambiguous keyed-DI
// resolution proves the sibling-provider-package split composes cleanly in one container. ─────────
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    // An empty storage registry: the SendGrid sender needs IFileStorageFactory, but these surfaces
    // only prove DI composition and never resolve an attachment.
    services.AddSharedKernelStorage();
    services.AddScoped<INotificationSenderIdentityResolver, NoOpSenderIdentityResolver>();

    services.AddSharedKernelNotifications();
    services.AddSendGridEmailNotifications(o => o.ApiKey = "consumer-verify-key");
    services.AddTwilioSmsNotifications(o =>
    {
        o.AccountSid = "ACconsumerverify";
        o.AuthToken = "consumer-verify-token";
        o.From = "+15005550006";
    });

    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();

    var emailSender = scope.ServiceProvider.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Email);
    var smsSender = scope.ServiceProvider.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Sms);

    Verify(emailSender is SendGridEmailNotificationSender, "NotificationChannel.Email resolves the SendGrid sender");
    Verify(smsSender is TwilioSmsNotificationSender, "NotificationChannel.Sms resolves the Twilio sender");
    Verify(!ReferenceEquals(emailSender, smsSender), "The two keyed registrations resolve distinct sender instances — no ambiguity");
}

Console.WriteLine("Surface 3 PASS: both Notifications providers registered together resolve unambiguously by NotificationChannel");

// ── Surface 4 (WO-072): omitting INotificationSenderIdentityResolver — clear, actionable DI
// failure at first resolution of the SendGrid sender, mirroring Surface 2's shape. ─────────────────
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    // An empty storage registry: the SendGrid sender needs IFileStorageFactory, but these surfaces
    // only prove DI composition and never resolve an attachment.
    services.AddSharedKernelStorage();
    services.AddSharedKernelNotifications();
    services.AddSendGridEmailNotifications(o => o.ApiKey = "consumer-verify-key");
    // Deliberately no INotificationSenderIdentityResolver registration.

    using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();

    InvalidOperationException? caught = null;
    try
    {
        scope.ServiceProvider.GetRequiredKeyedService<INotificationSender>(NotificationChannel.Email);
    }
    catch (InvalidOperationException ex)
    {
        caught = ex;
    }

    Verify(caught is not null, "Resolving the SendGrid sender throws InvalidOperationException, not a silent null/no-op, when INotificationSenderIdentityResolver is unregistered");
    Verify(
        caught!.Message.Contains(nameof(INotificationSenderIdentityResolver), StringComparison.Ordinal),
        "The DI resolution failure message names INotificationSenderIdentityResolver — actionable, not generic");
}

Console.WriteLine("Surface 4 PASS: omitting INotificationSenderIdentityResolver produces a clear, actionable DI resolution failure at first use");

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");

static void Verify(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL: {label}");

    Console.WriteLine($"  OK: {label}");
}

// ── Minimal integration event used solely to exercise IWebhookDispatcher.DispatchAsync ──────────
// The dispatcher routes on the [IntegrationEvent] name, so an undeclared event type would throw before the
// subscription lookup.
[IntegrationEvent("consumer-verify.webhooks.integration-event")]
internal sealed record ConsumerVerifyIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

// ── Store that always returns zero subscriptions — proves Surface 1's pipeline without needing a
// live subscriber endpoint or any 06.Persistence dependency. ────────────────────────────────────
internal sealed class NoSubscriptionsStore : IWebhookSubscriptionStore
{
    public Task<IReadOnlyList<WebhookSubscription>> GetActiveSubscriptionsAsync(string eventType, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<WebhookSubscription>>([]);
}

// ── No-op IEventPublisher stand-in — proves only SharedKernel.Integration.Webhooks's own DI
// surface; the real IEventPublisher wiring belongs to 07.Messaging and is out of scope here. ────
internal sealed class NoOpEventPublisher : IEventPublisher
{
    public Task<Result> PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
        where TEvent : class, IIntegrationEvent
        => Task.FromResult(Result.Success());

    public Task<Result> PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct)
        where TEvent : class, IIntegrationEvent
        => Task.FromResult(Result.Success());
}

// ── WO-072: minimal INotificationSenderIdentityResolver stand-in for Surface 3 (Surface 4
// deliberately omits registering this at all). ──────────────────────────────────────────────────
internal sealed class NoOpSenderIdentityResolver : INotificationSenderIdentityResolver
{
    public Task<NotificationSenderIdentity> ResolveAsync(NotificationChannel channel, CancellationToken ct) =>
        Task.FromResult(new NotificationSenderIdentity("no-reply@example.test"));
}
