using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Messaging.Abstractions.Options;

namespace SharedKernel.Messaging.MassTransit.Extensions;

/// <summary>
/// The entry point to the SharedKernel messaging layer: <c>AddSharedKernelMessaging</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SharedKernel messaging infrastructure, reading
    /// <see cref="MessagingOptions"/> from the <c>SharedKernel:Messaging</c> configuration section,
    /// and returns a <see cref="MessagingBusBuilder"/> for transport, consumer and feature wiring.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">
    /// The configuration to bind from — normally the root <c>builder.Configuration</c>, not a
    /// section. The section path is read from <see cref="MessagingOptions.SectionName"/>, so no
    /// call site names it.
    /// </param>
    /// <param name="configure">
    /// An optional action applied <em>after</em> binding, for values a host knows at startup but
    /// configuration does not. It overrides what was bound.
    /// </param>
    /// <returns>A <see cref="MessagingBusBuilder"/> for further fluent configuration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <strong>Prefer this overload.</strong> The inline-action one below cannot read a service
    /// name from configuration, so a host using it has to hard-code the value that becomes its
    /// queue-name prefix and its CloudEvents <c>source</c> — which is exactly the value that must
    /// differ between environments and deployments.
    /// </para>
    /// <para>
    /// Binding goes through <c>01.Core</c>'s <c>AddValidatedOptions</c>, never
    /// <c>services.Configure&lt;T&gt;(section)</c>: the latter binds without validating, so a
    /// missing <c>ServiceName</c> would surface as a malformed queue name at the first publish
    /// rather than as a startup failure.
    /// </para>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddSharedKernelMessaging(builder.Configuration)
    ///     .UseRabbitMq(builder.Configuration.GetConnectionString("rabbitmq")!)
    ///     .WithRetry()
    ///     .WithInboundRequestContext()
    ///     .AddConsumer&lt;OrderPlacedConsumer&gt;()
    ///     .Build();
    /// </code>
    /// <code language="json">
    /// {
    ///   "SharedKernel": { "Messaging": { "ServiceName": "order-service" } }
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Binds MessagingOptions from configuration, which is reflective. Use the inline-action " +
        "overload in a trimmed or AOT-published host.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "Binds MessagingOptions from configuration, which is reflective. Use the inline-action " +
        "overload in a trimmed or AOT-published host.")]
    public static MessagingBusBuilder AddSharedKernelMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<MessagingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<MessagingOptions>(
            configuration.GetSection(MessagingOptions.SectionName));

        RegisterValidator(services);

        if (configure is not null)
            services.Configure(configure);

        // The builder is handed no inline action: Build() must not validate a partially-configured
        // instance it assembled itself, because the bound values it would be missing are exactly
        // the ones the host put in configuration. ValidateOnStart, registered by
        // AddValidatedOptions, is what fails a bad section here — at startup, before the first
        // message.
        return new MessagingBusBuilder(services, configure: null);
    }

    /// <summary>
    /// Registers the SharedKernel messaging infrastructure with <see cref="MessagingOptions"/>
    /// supplied in code, and returns a <see cref="MessagingBusBuilder"/> for transport, consumer
    /// and feature wiring.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    /// An action configuring <see cref="MessagingOptions"/>. Optional only so that a host can bind
    /// the section itself beforehand; when it is omitted and nothing else has configured the
    /// options, startup fails with a message naming <c>ServiceName</c>.
    /// </param>
    /// <returns>A <see cref="MessagingBusBuilder"/> for further fluent configuration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Use this overload for a test host, or for a service whose name genuinely is a constant.
    /// For anything reading its configuration, prefer
    /// <see cref="AddSharedKernelMessaging(IServiceCollection, IConfiguration, Action{MessagingOptions})"/>.
    /// </para>
    /// <para>
    /// The action is also handed to the builder, so <see cref="MessagingBusBuilder.Build"/> can
    /// resolve the service name for endpoint naming without building a second service provider to
    /// read it back.
    /// </para>
    /// <example>
    /// <code>
    /// services
    ///     .AddSharedKernelMessaging(o =&gt; o.ServiceName = "order-service")
    ///     .UseRabbitMq("rabbitmq://localhost")
    ///     .WithRetry()
    ///     .AddConsumer&lt;OrderPlacedConsumer&gt;()
    ///     .Build();
    /// </code>
    /// </example>
    /// </remarks>
    public static MessagingBusBuilder AddSharedKernelMessaging(
        this IServiceCollection services,
        Action<MessagingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ValidateOnStart matters most on the path where no inline action was supplied: a host that
        // bound the section itself gets its misconfiguration at startup rather than at first publish.
        var optionsBuilder = services.AddOptions<MessagingOptions>().ValidateOnStart();

        if (configure is not null)
            optionsBuilder.Configure(configure);

        RegisterValidator(services);

        // The captured action lets Build() validate inline without calling BuildServiceProvider(),
        // which would create a second root provider.
        return new MessagingBusBuilder(services, configure);
    }

    /// <summary>
    /// Registers <see cref="MessagingOptionsValidator"/> once, however many times
    /// <c>AddSharedKernelMessaging</c> is called.
    /// </summary>
    /// <remarks>
    /// <c>TryAddEnumerable</c> rather than <c>AddSingleton</c>: options validators are resolved as
    /// a collection, so a duplicate registration would report the same failure twice in one startup
    /// exception.
    /// </remarks>
    private static void RegisterValidator(IServiceCollection services) =>
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>());
}
