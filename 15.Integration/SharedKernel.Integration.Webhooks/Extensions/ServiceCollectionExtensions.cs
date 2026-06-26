using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
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
    /// <see cref="WebhookSignatureProvider"/> (singleton), <see cref="IWebhookDispatcher"/> (scoped),
    /// and the named <see cref="HttpClient"/> used for outbound webhook delivery, wired with
    /// <c>Microsoft.Extensions.Http.Resilience</c>'s standard resilience handler.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Optional callback to override default <see cref="WebhookDeliveryOptions"/> values.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// Deliberately does not register <see cref="Subscriptions.IWebhookSubscriptionStore"/> (required —
    /// the consuming service must register its own implementation or DI resolution fails at first
    /// dispatch) or any <see cref="IWebhookDeliveryObserver"/> (optional — see
    /// <see cref="WithDeliveryObserver{TObserver}"/>).
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
        services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();

        services
            .AddHttpClient(WebhookHttpClientName.Name)
            .AddStandardResilienceHandler(resilienceOptions =>
            {
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
}
