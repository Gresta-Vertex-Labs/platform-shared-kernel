using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Abstractions.Options;

namespace SharedKernel.Integration.Notifications.Abstractions.Extensions;

/// <summary>DI registration entry points shared by every outbound notification provider.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="NotificationDeliveryOptions"/>, validated eagerly at startup.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Optional callback to override default <see cref="NotificationDeliveryOptions"/> values.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// Registers <see cref="NotificationDeliveryOptions"/> only. Does NOT register any
    /// <see cref="Notifications.INotificationSender"/> (provider-supplied, keyed — call
    /// <c>AddSendGridEmailNotifications</c>/<c>AddTwilioSmsNotifications</c> or an equivalent
    /// provider registration) or <see cref="INotificationSenderIdentityResolver"/> (required — the
    /// consuming service must register its own implementation or DI resolution fails at first send,
    /// mirroring <c>IWebhookSubscriptionStore</c>'s "required, consumer-supplied" precedent).
    /// </remarks>
    public static IServiceCollection AddSharedKernelNotifications(
        this IServiceCollection services,
        Action<NotificationDeliveryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services
            .AddOptions<NotificationDeliveryOptions>()
            .BindConfiguration("SharedKernel:Integration:Notifications")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TObserver"/> as a scoped <see cref="INotificationDeliveryObserver"/>.
    /// </summary>
    /// <typeparam name="TObserver">The observer implementation to register.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// Additive — mirrors <c>WithDeliveryObserver&lt;T&gt;()</c>'s exact registration shape. Multiple
    /// calls accumulate; every registered observer fires for every send attempt and completion, in
    /// registration order.
    /// </remarks>
    public static IServiceCollection WithNotificationDeliveryObserver<TObserver>(this IServiceCollection services)
        where TObserver : class, INotificationDeliveryObserver
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<INotificationDeliveryObserver, TObserver>();

        return services;
    }
}
