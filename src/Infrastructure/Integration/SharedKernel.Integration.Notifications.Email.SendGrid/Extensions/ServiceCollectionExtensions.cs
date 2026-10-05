using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Options;
using SharedKernel.Integration.Notifications.Email.SendGrid.Options;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Extensions;

/// <summary>DI registration entry point for the SendGrid email notification provider.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SendGridNotificationOptions"/> (validated eagerly at startup), the named
    /// <see cref="System.Net.Http.HttpClient"/> used for SendGrid delivery (wired with
    /// <c>Microsoft.Extensions.Http.Resilience</c>'s standard resilience handler, configured from
    /// the shared <see cref="NotificationDeliveryOptions"/>), and the keyed
    /// <see cref="Notifications.INotificationSender"/> for <see cref="NotificationChannel.Email"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Callback to supply <see cref="SendGridNotificationOptions.ApiKey"/> and any other overrides.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// Requires <see cref="SharedKernel.Integration.Notifications.Abstractions.Observability.INotificationSenderIdentityResolver"/>
    /// and <see cref="SharedKernel.Storage.IFileStorageFactory"/> (from
    /// <c>services.AddSharedKernelStorage()</c> plus a provider and the stores attachments live in) to
    /// already be registered by the consuming service — this method does not register either; DI
    /// resolution fails at first send if either is missing. Each attachment is read from the store its
    /// <c>FileReference</c> names, through that reference's tenant view when it carries a tenant.
    /// </remarks>
    public static IServiceCollection AddSendGridEmailNotifications(
        this IServiceCollection services,
        Action<SendGridNotificationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<SendGridNotificationOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddKeyedScoped<INotificationSender, SendGridEmailNotificationSender>(NotificationChannel.Email);

        services
            .AddHttpClient(SendGridHttpClientName.Name)
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions resilienceOptions, IServiceProvider serviceProvider) =>
            {
                var deliveryOptions = serviceProvider.GetRequiredService<IOptions<NotificationDeliveryOptions>>().Value;

                // Same field-mapping formula as SharedKernel.Integration.Webhooks/P-421 — resolved
                // from the app's IServiceProvider inside the resilience handler's own configuration
                // callback, never left to the library's own built-in defaults. Not extracted to a
                // shared helper in .Abstractions: that package is deliberately zero-I/O and carries
                // no Microsoft.Extensions.Http.Resilience dependency of its own.
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

                // Send-concurrency respected via the resilience pipeline's own rate-limiter stage —
                // no bespoke per-provider throttle type.
                resilienceOptions.RateLimiter.DefaultRateLimiterOptions.PermitLimit = Math.Max(1, deliveryOptions.MaxConcurrentSends);
                resilienceOptions.RateLimiter.DefaultRateLimiterOptions.QueueLimit = Math.Max(1, deliveryOptions.MaxConcurrentSends);
            });

        return services;
    }
}
