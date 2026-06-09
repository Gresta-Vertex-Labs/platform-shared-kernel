using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Messaging.Abstractions.Options;

namespace SharedKernel.Messaging.MassTransit.Extensions;

/// <summary>
/// IServiceCollection extension methods for the SharedKernel messaging layer.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SharedKernel messaging infrastructure and returns a <see cref="MessagingBusBuilder"/>
    /// for fluent transport, consumer, retry, and outbox configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    /// Optional action to configure <see cref="MessagingOptions"/> inline.
    /// Alternatively, bind <c>"SharedKernel:Messaging"</c> from <c>IConfiguration</c> before calling
    /// this method using <c>services.Configure&lt;MessagingOptions&gt;(config.GetSection(MessagingOptions.SectionName))</c>.
    /// </param>
    /// <returns>A <see cref="MessagingBusBuilder"/> for further fluent configuration.</returns>
    /// <example>
    /// <code>
    /// services
    ///     .AddSharedKernelMessaging(o => o.ServiceName = "order-service")
    ///     .UseRabbitMq("rabbitmq://localhost")
    ///     .WithRetry()
    ///     .AddConsumer&lt;OrderPlacedConsumer&gt;()
    ///     .Build();
    /// </code>
    /// </example>
    public static MessagingBusBuilder AddSharedKernelMessaging(
        this IServiceCollection services,
        Action<MessagingOptions>? configure = null)
    {
        // Register MessagingOptions with eager startup validation.
        // ValidateOnStart() ensures that if the deferred-binding path is used (no inline action),
        // a misconfigured ServiceName surfaces at host startup rather than at first message publish.
        var optionsBuilder = services.AddOptions<MessagingOptions>()
            .ValidateOnStart();

        if (configure is not null)
            optionsBuilder.Configure(configure);

        // Register the validator used by both the inline Build() path and ValidateOnStart().
        services.AddSingleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>();

        // Pass the captured configure action to the builder so Build() can validate inline
        // without calling BuildServiceProvider() (which would create a second root provider).
        return new MessagingBusBuilder(services, configure);
    }
}
