using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Integration.Webhooks.Options;
using SharedKernel.Integration.Webhooks.Signing;

namespace SharedKernel.Integration.Webhooks.Extensions;

/// <summary>DI registration entry points for the outbound webhook delivery pipeline.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WebhookDeliveryOptions"/> (validated eagerly at startup),
    /// <see cref="WebhookSignatureProvider"/> (singleton), the default
    /// <see cref="IWebhookUrlValidator"/> (<see cref="PrivateNetworkWebhookUrlValidator"/>, singleton),
    /// <see cref="IWebhookDispatcher"/> (scoped), and the named <see cref="HttpClient"/> used for
    /// outbound webhook delivery, wired with <c>Microsoft.Extensions.Http.Resilience</c>'s standard
    /// resilience handler.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Optional callback to override default <see cref="WebhookDeliveryOptions"/> values.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Deliberately does not register <see cref="Subscriptions.IWebhookSubscriptionStore"/> (required —
    /// the consuming service must register its own implementation or DI resolution fails at first
    /// dispatch) or any <see cref="IWebhookDeliveryObserver"/> (optional — see
    /// <see cref="WithDeliveryObserver{TObserver}"/>).
    /// </para>
    /// <para>
    /// The named <see cref="HttpClient"/>'s resilience pipeline is configured from the bound
    /// <see cref="WebhookDeliveryOptions"/> via the exact field mapping:
    /// <c>Retry.MaxRetryAttempts = Math.Max(1, MaxAttempts - 1)</c> (the resilience library counts
    /// retries <i>after</i> the initial attempt; <see cref="WebhookDeliveryResult.Attempts"/> counts
    /// the initial attempt too — the two are off-by-one by definition, not by bug). Polly's own
    /// <c>RetryStrategyOptions.MaxRetryAttempts</c> carries a hard <c>[Range(1, int.MaxValue)]</c>
    /// floor and cannot itself express "zero retries" — when <c>MaxAttempts == 1</c> (no retry at
    /// all), the floor of 1 is kept to satisfy that validator, and <c>Retry.ShouldHandle</c> is
    /// instead short-circuited to <see langword="false"/> so no retry is ever actually triggered.
    /// <c>Retry.Delay = BaseBackoffDelay</c>; <c>Retry.BackoffType = DelayBackoffType.Exponential</c>;
    /// <c>Retry.MaxDelay = MaxBackoffDelay</c>; <c>AttemptTimeout.Timeout = RequestTimeout</c>;
    /// <c>TotalRequestTimeout.Timeout = (RequestTimeout + MaxBackoffDelay) * MaxAttempts</c> (a
    /// documented worst-case bound: every attempt takes at most <c>RequestTimeout</c>, followed by at
    /// most <c>MaxBackoffDelay</c> before the next attempt, repeated <c>MaxAttempts</c> times) —
    /// resolved from the app's <see cref="IServiceProvider"/> inside
    /// <c>IHttpStandardResiliencePipelineBuilder.Configure((options, serviceProvider) => ...)</c>,
    /// never left to the library's own built-in defaults.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelWebhooks(
        this IServiceCollection services,
        Action<WebhookDeliveryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services
            .AddOptions<WebhookDeliveryOptions>()
            .BindConfiguration("SharedKernel:Integration:Webhooks")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.AddSingleton<WebhookSignatureProvider>();
        services.TryAddSingleton<IWebhookUrlValidator, PrivateNetworkWebhookUrlValidator>();
        services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();

        services
            .AddHttpClient(WebhookHttpClientName.Name)
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions resilienceOptions, IServiceProvider serviceProvider) =>
            {
                var deliveryOptions = serviceProvider.GetRequiredService<IOptions<WebhookDeliveryOptions>>().Value;
                var desiredRetries = deliveryOptions.MaxAttempts - 1;

                // Polly's own RetryStrategyOptions.MaxRetryAttempts carries a hard [Range(1, ...)]
                // floor — it cannot itself express "zero retries". When MaxAttempts == 1 (no retry
                // at all), the floor of 1 is kept to satisfy that validator, and ShouldHandle below
                // is short-circuited to never actually trigger a retry instead.
                resilienceOptions.Retry.MaxRetryAttempts = Math.Max(1, desiredRetries);
                resilienceOptions.Retry.Delay = deliveryOptions.BaseBackoffDelay;
                resilienceOptions.Retry.BackoffType = DelayBackoffType.Exponential;
                resilienceOptions.Retry.MaxDelay = deliveryOptions.MaxBackoffDelay;
                resilienceOptions.Retry.OnRetry = args =>
                {
                    var requestMessage = args.Outcome.Result?.RequestMessage;
                    if (requestMessage is not null &&
                        requestMessage.Options.TryGetValue(WebhookAttemptTracker.Key, out var tracker))
                    {
                        tracker.RecordRetry();
                    }

                    return default;
                };

                if (desiredRetries <= 0)
                {
                    resilienceOptions.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
                }

                resilienceOptions.AttemptTimeout.Timeout = deliveryOptions.RequestTimeout;
                resilienceOptions.TotalRequestTimeout.Timeout =
                    (deliveryOptions.RequestTimeout + deliveryOptions.MaxBackoffDelay) * deliveryOptions.MaxAttempts;
            });

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TObserver"/> as a scoped <see cref="IWebhookDeliveryObserver"/>.
    /// </summary>
    /// <typeparam name="TObserver">The observer implementation to register.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// Additive — multiple calls accumulate. Every registered observer fires for every delivery
    /// attempt and completion, in registration order.
    /// </remarks>
    public static IServiceCollection WithDeliveryObserver<TObserver>(this IServiceCollection services)
        where TObserver : class, IWebhookDeliveryObserver
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IWebhookDeliveryObserver, TObserver>();

        return services;
    }

    /// <summary>
    /// Overrides the default <see cref="PrivateNetworkWebhookUrlValidator"/> registration with
    /// <typeparamref name="TValidator"/>.
    /// </summary>
    /// <typeparam name="TValidator">The <see cref="IWebhookUrlValidator"/> implementation to register.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// The last call wins — a single active validator, unlike <see cref="WithDeliveryObserver{TObserver}"/>'s
    /// additive registration — regardless of whether this is called before or after
    /// <see cref="AddSharedKernelWebhooks"/>. Exists for consuming services with a non-default
    /// target-network policy; the SSRF-guard default posture must not be silently disabled — prefer
    /// <see cref="WebhookDeliveryOptions.AllowPrivateNetworkTargets"/> for the common "allow internal
    /// staging targets" case instead of a full custom validator.
    /// </remarks>
    public static IServiceCollection WithUrlValidator<TValidator>(this IServiceCollection services)
        where TValidator : class, IWebhookUrlValidator
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IWebhookUrlValidator>();
        services.AddSingleton<IWebhookUrlValidator, TValidator>();

        return services;
    }
}
