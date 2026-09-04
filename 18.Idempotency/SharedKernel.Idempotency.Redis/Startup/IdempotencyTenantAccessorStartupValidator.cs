using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Messaging.Abstractions.TenantContext;

namespace SharedKernel.Idempotency.Redis.Startup;

/// <summary>
/// Fails fast at <see cref="IHost.StartAsync(CancellationToken)"/> when no
/// <see cref="ITenantContextAccessor"/> implementation has been registered, instead of letting the
/// first store call throw an obscure <see cref="InvalidOperationException"/> from DI resolution
/// (D-09).
/// </summary>
/// <remarks>
/// An <see cref="IHostedService"/> — rather than a check performed inline inside
/// <c>AddSharedKernelRedisIdempotency</c> — because <see cref="IServiceCollection"/> registration
/// order is not guaranteed: a consumer may call <c>AddSharedKernelRedisIdempotency</c> before
/// registering their own <see cref="ITenantContextAccessor"/> implementation later in the same
/// composition root. Deferring the check to host startup (after the full container is built)
/// makes the check registration-order-independent, and — unlike relying on
/// <see cref="Microsoft.Extensions.DependencyInjection.ServiceProviderOptions.ValidateOnBuild"/>,
/// which the generic host only enables by default in the <c>Development</c> environment — this
/// check runs unconditionally in every environment.
/// </remarks>
internal sealed class IdempotencyTenantAccessorStartupValidator(IServiceProvider serviceProvider) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // ITenantContextAccessor is conventionally registered Scoped (mirroring
        // MessagingBusBuilder.WithTenantContext<TAccessor>()), so resolve it through a short-lived
        // scope rather than the root provider directly — avoids both a ValidateScopes failure and
        // an accidental captive-dependency singleton instance of a scoped service.
        using var scope = serviceProvider.CreateScope();
        var accessor = scope.ServiceProvider.GetService<ITenantContextAccessor>();

        if (accessor is null)
        {
            throw new InvalidOperationException(
                "SharedKernel.Idempotency.Redis requires an ITenantContextAccessor implementation " +
                "to be registered before the application starts (SharedKernel.Messaging.Abstractions." +
                "TenantContext.ITenantContextAccessor). Register your own implementation against your " +
                "service's real tenant-identity source (e.g. SharedKernel.Security.Abstractions." +
                "ITenantProvider), or call SharedKernel.Messaging.MassTransit's " +
                "MessagingBusBuilder.WithTenantContext<TAccessor>() if this service already uses " +
                "SharedKernel.Messaging. See this package's README.md for a worked example.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
