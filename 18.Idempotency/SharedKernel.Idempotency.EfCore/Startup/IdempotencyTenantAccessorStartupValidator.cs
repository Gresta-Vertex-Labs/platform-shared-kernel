using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Messaging.Abstractions.TenantContext;

namespace SharedKernel.Idempotency.EfCore.Startup;

/// <summary>
/// Fails fast at <see cref="IHost.StartAsync(CancellationToken)"/> when no
/// <see cref="ITenantContextAccessor"/> implementation has been registered, instead of letting the
/// first store call throw an obscure <see cref="InvalidOperationException"/> from DI resolution
/// (D-09).
/// </summary>
/// <remarks>
/// Not shared with <c>SharedKernel.Idempotency.Redis</c>'s identically-named type — this domain
/// deliberately has no shared <c>.Core</c> package (18.Idempotency/CLAUDE.md, "Code shared by both
/// providers: Nowhere — duplicate it"). See that type's remarks for the full reasoning behind
/// deferring this check to host startup rather than performing it inline inside the DI extension
/// method.
/// </remarks>
internal sealed class IdempotencyTenantAccessorStartupValidator(IServiceProvider serviceProvider) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var accessor = scope.ServiceProvider.GetService<ITenantContextAccessor>();

        if (accessor is null)
        {
            throw new InvalidOperationException(
                "SharedKernel.Idempotency.EfCore requires an ITenantContextAccessor implementation " +
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
