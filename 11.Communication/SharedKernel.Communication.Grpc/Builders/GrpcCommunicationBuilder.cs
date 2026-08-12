using GrpcCore = Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Grpc.Interceptors;
using SharedKernel.Communication.Grpc.Options;
using SharedKernel.Communication.Internal.Resolvers;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Communication.Grpc.Builders;

/// <summary>Default implementation of <see cref="IGrpcCommunicationBuilder"/>.</summary>
internal sealed class GrpcCommunicationBuilder : IGrpcCommunicationBuilder
{
    // G-10: Resolver presence is captured once at construction time.
    // Services.Any(...) is called exactly once — never inside AddGrpcClient<TClient>.
    private readonly bool _resolverRegistered;

    // Stateless — validates the just-constructed options instance directly, at the point of
    // consumption, since GrpcClientOptions is never resolved via IOptions<GrpcClientOptions>.Value
    // (P-359/WO-056), mirroring RestClientOptionsValidator's identical validate-at-point-of-consumption
    // pattern (see RestCommunicationBuilder.AddRestClient<TClient>).
    private static readonly GrpcClientOptionsValidator OptionsValidator = new();

    internal GrpcCommunicationBuilder(IServiceCollection services)
    {
        Services = services;
        _resolverRegistered = services.Any(d => d.ServiceType == typeof(IServiceEndpointResolver));
    }

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public IGrpcCommunicationBuilder AddGrpcClient<TClient>(
        string? address = null,
        Action<GrpcClientOptions>? configure = null)
        where TClient : class
    {
        var options = new GrpcClientOptions { Address = address };
        configure?.Invoke(options);

        var clientName = typeof(TClient).Name;

        // P-359/D-29: validate the just-constructed instance immediately after configure?.Invoke(options)
        // and before it is applied to the channel/call-options pipeline — the registered
        // IValidateOptions<GrpcClientOptions> can never structurally fire on its own, since this type is
        // never resolved via IOptions<GrpcClientOptions>.Value. Mirrors R-23's .Rest pattern (D-28).
        var validationResult = OptionsValidator.Validate(clientName, options);
        if (validationResult.Failed)
        {
            throw new OptionsValidationException(clientName, typeof(GrpcClientOptions), validationResult.Failures);
        }

        var hasAddress = !string.IsNullOrWhiteSpace(options.Address);

        // G-10: use pre-captured _resolverRegistered, not a fresh Services.Any(...) probe.
        if (!hasAddress && !_resolverRegistered)
        {
            throw new InvalidOperationException(
                $"GrpcClientOptions for '{typeof(TClient).Name}' requires either an Address or a registered " +
                $"IServiceEndpointResolver. Call AddK8sServiceDiscovery() or AddStaticServiceDiscovery() " +
                $"before registering typed gRPC clients without an Address.");
        }

        var enableRetry = options.EnableRetry;
        var deadlineSeconds = options.DeadlineSeconds;

        IHttpClientBuilder clientBuilder;

        if (hasAddress)
        {
            // Address is known at registration time — configure via GrpcClientFactoryOptions.
            // Uses the (IServiceProvider, GrpcClientFactoryOptions) overload — not the single-arg one —
            // solely to reach `sp` for the P-359 IClock resolution below, mirroring the pattern the
            // service-discovery branch already used for IServiceEndpointResolver resolution.
            clientBuilder = Services.AddGrpcClient<TClient>((sp, o) =>
            {
                o.Address = new Uri(options.Address!);
                ConfigureDeadline(o, sp, deadlineSeconds);
            });
        }
        else
        {
            // G-09 / G-12: Address omitted — resolve via IServiceEndpointResolver at channel-creation time.
            // GrpcClientFactoryOptions.Address is configured using a factory action that resolves
            // the address synchronously from DI. ConfigureChannel sets the channel ServiceConfig.
            clientBuilder = Services.AddGrpcClient<TClient>((sp, o) =>
            {
                var resolver = sp.GetRequiredService<IServiceEndpointResolver>();
                // ResolveAsync never throws — safe to GetAwaiter().GetResult() here.
                // Called once per channel (channel is cached as singleton by ClientFactory).
                var uri = resolver.ResolveAsync(clientName, CancellationToken.None)
                    .GetAwaiter().GetResult();
                o.Address = uri;
                ConfigureDeadline(o, sp, deadlineSeconds);
            });
        }

        // Apply gRPC retry service config when enabled.
        if (enableRetry)
        {
            clientBuilder.ConfigureChannel(channelOptions =>
            {
                channelOptions.ServiceConfig = BuildRetryServiceConfig();
            });
        }

        // Register both interceptors globally for this client via Grpc.Net.ClientFactory.
        // Correlation tracing first (reads Activity.Current), then tenant ID.
        clientBuilder
            .AddInterceptor<CorrelationTracingInterceptor>(InterceptorScope.Channel)
            .AddInterceptor<TenantIdInterceptor>(InterceptorScope.Channel);

        return this;
    }

    private static ServiceConfig BuildRetryServiceConfig() =>
        new()
        {
            MethodConfigs =
            {
                new MethodConfig
                {
                    Names = { MethodName.Default },
                    RetryPolicy = new RetryPolicy
                    {
                        MaxAttempts = 3,
                        InitialBackoff = TimeSpan.FromMilliseconds(500),
                        MaxBackoff = TimeSpan.FromSeconds(5),
                        BackoffMultiplier = 1.5,
                        RetryableStatusCodes =
                        {
                            GrpcCore.StatusCode.Unavailable,
                            GrpcCore.StatusCode.DeadlineExceeded
                        }
                    }
                }
            }
        };

    /// <summary>
    /// Wires <paramref name="deadlineSeconds"/> into a real, enforced per-call deadline (P-359/WO-056).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Grpc.Net.ClientFactory</c> 2.80.0 has no <c>IHttpClientBuilder.ConfigureDefaultCallOptions(...)</c>
    /// method — that API shape was an unverified assumption in the original design (D-29). Verified
    /// empirically by reflecting over the real 2.80.0 assembly: the actual mechanism is
    /// <see cref="GrpcClientFactoryOptions.CallOptionsActions"/>, a list of
    /// <c>Action&lt;CallOptionsContext&gt;</c> delegates invoked by the generated client for every
    /// outgoing call. <see cref="CallOptionsContext"/> itself exposes both <c>CallOptions</c> (mutable,
    /// starts as whatever the caller supplied) and <c>ServiceProvider</c>, but this method resolves
    /// <see cref="IClock"/> once, from the <paramref name="serviceProvider"/> already available in the
    /// same registration-time closure the address-resolution logic uses (never a fresh
    /// <c>DateTime.UtcNow</c>/<c>DateTimeOffset.UtcNow</c> call, per SK0001) — the deadline instant
    /// itself is still computed fresh at call time, since <see cref="IClock.UtcNow"/> is read inside the
    /// per-call action, not captured ahead of time.
    /// </para>
    /// <para>
    /// Never overwrites a per-call deadline the caller already supplied through the generated client's
    /// own <c>CallOptions</c> overload — mirrors this domain's "caller-supplied value always wins"
    /// convention already applied to <c>x-correlation-id</c>/<c>x-tenant-id</c> metadata injection.
    /// </para>
    /// </remarks>
    private static void ConfigureDeadline(
        GrpcClientFactoryOptions clientFactoryOptions,
        IServiceProvider serviceProvider,
        int deadlineSeconds)
    {
        var clock = serviceProvider.GetRequiredService<IClock>();

        clientFactoryOptions.CallOptionsActions.Add(context =>
        {
            if (context.CallOptions.Deadline is not null)
            {
                return;
            }

            var deadline = clock.UtcNow.UtcDateTime.AddSeconds(deadlineSeconds);
            context.CallOptions = context.CallOptions.WithDeadline(deadline);
        });
    }
}
