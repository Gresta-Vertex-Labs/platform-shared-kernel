using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Options;
using SharedKernel.Integration.Notifications.Sms.Twilio.Options;

namespace SharedKernel.Integration.Notifications.Sms.Twilio.Extensions;

/// <summary>DI registration entry point for the Twilio SMS notification provider.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TwilioNotificationOptions"/> (validated eagerly at startup), the named
    /// <see cref="System.Net.Http.HttpClient"/> used for Twilio delivery (wired with
    /// <c>Microsoft.Extensions.Http.Resilience</c>'s standard resilience handler, configured from
    /// the shared <see cref="NotificationDeliveryOptions"/>), and the keyed
    /// <see cref="Notifications.INotificationSender"/> for <see cref="NotificationChannel.Sms"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Callback to supply <see cref="TwilioNotificationOptions.AccountSid"/>/<see cref="TwilioNotificationOptions.AuthToken"/> and any other overrides.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddTwilioSmsNotifications(
        this IServiceCollection services,
        Action<TwilioNotificationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<TwilioNotificationOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddKeyedScoped<INotificationSender, TwilioSmsNotificationSender>(NotificationChannel.Sms);

        services
            .AddHttpClient(TwilioHttpClientName.Name)
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions resilienceOptions, IServiceProvider serviceProvider) =>
            {
                var deliveryOptions = serviceProvider.GetRequiredService<IOptions<NotificationDeliveryOptions>>().Value;

                // Same field-mapping formula as SharedKernel.Integration.Webhooks/P-421 and
                // .Email.SendGrid — resolved from the app's IServiceProvider inside the resilience
                // handler's own configuration callback, never left to the library's own built-in
                // defaults. Not extracted to a shared helper in .Abstractions: that package is
                // deliberately zero-I/O and carries no Microsoft.Extensions.Http.Resilience
                // dependency of its own.
                var desiredRetries = deliveryOptions.MaxAttempts - 1;

                resilienceOptions.Retry.MaxRetryAttempts = Math.Max(1, desiredRetries);
                resilienceOptions.Retry.Delay = deliveryOptions.BaseBackoffDelay;
                resilienceOptions.Retry.BackoffType = DelayBackoffType.Exponential;
                resilienceOptions.Retry.MaxDelay = deliveryOptions.MaxBackoffDelay;

                if (desiredRetries <= 0)
                {
                    resilienceOptions.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
                }

                resilienceOptions.AttemptTimeout.Timeout = deliveryOptions.RequestTimeout;
                resilienceOptions.TotalRequestTimeout.Timeout =
                    (deliveryOptions.RequestTimeout + deliveryOptions.MaxBackoffDelay) * deliveryOptions.MaxAttempts;

                // Twilio's per-account throughput limits are respected entirely via this shared
                // rate-limiter stage — no bespoke Twilio-specific throttle type invented.
                resilienceOptions.RateLimiter.DefaultRateLimiterOptions.PermitLimit = Math.Max(1, deliveryOptions.MaxConcurrentSends);
                resilienceOptions.RateLimiter.DefaultRateLimiterOptions.QueueLimit = Math.Max(1, deliveryOptions.MaxConcurrentSends);
            });

        return services;
    }
}
