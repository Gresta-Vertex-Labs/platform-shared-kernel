using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.MassTransit.AzureServiceBus.Diagnostics;

namespace SharedKernel.Messaging.MassTransit.AzureServiceBus.DeadLetter;

/// <summary>
/// Logs an advisory warning at host startup when <c>WithDeadLetterPolicy()</c> was called while
/// the Azure Service Bus transport is configured.
/// </summary>
/// <remarks>
/// <para>
/// <c>DeadLetterOptions</c> is RabbitMQ-only. Azure Service Bus dead-lettering is entirely
/// transport-native — driven by the queue/subscription <c>MaxDeliveryCount</c> configured at the
/// Azure resource level, not through this domain's configuration surface. Calling
/// <c>WithDeadLetterPolicy()</c> under an Azure Service Bus transport is therefore a no-op.
/// </para>
/// <para>
/// Registered as a singleton <see cref="IHostedService"/> by the Azure Service Bus transport during <c>MessagingBusBuilder.Build()</c>
/// only when both <c>WithDeadLetterPolicy()</c> was called and the Azure Service Bus transport is
/// configured. Mirrors the deferred-advisory-at-startup pattern established by
/// <c>SchemaEvolution.TranslatorRegistrationValidationHostedService</c> — <c>Build()</c> must not
/// call <c>Services.BuildServiceProvider()</c> to resolve a real <see cref="ILogger"/>, so the
/// advisory check is deferred to a startup <see cref="IHostedService"/> instead. Does not throw and
/// does not affect bus lifecycle.
/// </para>
/// </remarks>
internal sealed class DeadLetterPolicyAdvisoryHostedService : IHostedService
{
    private readonly ILogger<DeadLetterPolicyAdvisoryHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DeadLetterPolicyAdvisoryHostedService"/>.
    /// </summary>
    /// <param name="logger">Logger used to emit the advisory warning.</param>
    public DeadLetterPolicyAdvisoryHostedService(ILogger<DeadLetterPolicyAdvisoryHostedService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        AzureServiceBusLog.DeadLetterPolicyIgnored(_logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
