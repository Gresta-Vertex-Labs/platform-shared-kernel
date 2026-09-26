using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Context;

namespace SharedKernel.Messaging.MassTransit.Context;

/// <summary>
/// Warns at host startup when something registered an <c>IRequestContext</c> after
/// <c>WithInboundRequestContext()</c> ran, which would silently stop a message's identity from
/// reaching its consumer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The failure this catches.</strong> Registration order decides which
/// <c>IRequestContext</c> wins: the container resolves the last one registered. A composition root
/// that calls <c>AddSharedKernelMessaging(...).WithInboundRequestContext().Build()</c> and only
/// afterwards calls <c>AddSharedKernelRequestContext()</c> ends up with the HTTP-backed context
/// everywhere — including inside consumers, where it reports no tenant. Nothing throws; consumers
/// just quietly fail closed on every tenant-scoped write, which looks like a persistence bug and
/// is not one.
/// </para>
/// <para>
/// Advisory only, matching <c>TranslatorRegistrationValidationHostedService</c> and
/// <c>DeadLetterPolicyAdvisoryHostedService</c>. Throwing here would take down a host over a
/// registration a service might have made deliberately.
/// </para>
/// </remarks>
internal sealed partial class RequestContextRegistrationAdvisoryHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RequestContextRegistrationAdvisoryHostedService> _logger;

    /// <summary>Initialises the advisory service.</summary>
    /// <param name="serviceProvider">The root provider, used to open one throwaway scope.</param>
    /// <param name="logger">Logger used to emit the advisory warning.</param>
    public RequestContextRegistrationAdvisoryHostedService(
        IServiceProvider serviceProvider,
        ILogger<RequestContextRegistrationAdvisoryHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // A scope of its own: IRequestContext is scoped, and resolving it off the root provider
        // throws in a container with scope validation on (the default for the ASP.NET Core host
        // in Development).
        using var scope = _serviceProvider.CreateScope();

        IRequestContext? resolved;
        try
        {
            resolved = scope.ServiceProvider.GetService<IRequestContext>();
        }
        catch (Exception ex)
        {
            // A service's own IRequestContext may legitimately refuse to materialise outside a real
            // request. That is not the misconfiguration being looked for, so it is recorded and
            // ignored rather than escalated.
            LogRequestContextNotResolvable(ex);
            return Task.CompletedTask;
        }

        if (resolved is not MessageAwareRequestContext)
            LogRequestContextOverridden(resolved?.GetType().Name ?? "none");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 7011,
        Level = LogLevel.Warning,
        Message = "WithInboundRequestContext() was called, but IRequestContext resolves to " +
            "{ResolvedType} instead of the message-aware context. Something registered " +
            "IRequestContext after the messaging bus was built, and the last registration wins, so " +
            "a consumer will see no tenant and no actor however the message was published. Move " +
            "the other registration (typically AddSharedKernelRequestContext()) before " +
            "AddSharedKernelMessaging(...).Build().")]
    private partial void LogRequestContextOverridden(string resolvedType);

    [LoggerMessage(
        EventId = 7012,
        Level = LogLevel.Debug,
        Message = "IRequestContext could not be resolved while checking the inbound message-context " +
            "registration. The check was skipped; inbound context itself is unaffected.")]
    private partial void LogRequestContextNotResolvable(Exception exception);
}
