using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Messaging.MassTransit.SchemaEvolution;

/// <summary>
/// Runs <see cref="TranslatorRegistrationValidator"/> once at host startup for every
/// <c>WithVersionTranslator&lt;TOld, TNew, TTranslator&gt;()</c> registration captured by
/// <c>MessagingBusBuilder.Build()</c>.
/// </summary>
/// <remarks>
/// Registered as a singleton <see cref="IHostedService"/> by
/// <c>MessagingBusBuilder.Build()</c> only when at least one
/// <c>WithVersionTranslator&lt;TOld, TNew, TTranslator&gt;()</c> registration was made. Logs an
/// advisory <see cref="LogLevel.Warning"/> for each registration where no consumer for the new
/// schema type was found in this service. Does not throw and does not affect bus lifecycle.
/// </remarks>
internal sealed class TranslatorRegistrationValidationHostedService : IHostedService
{
    private readonly IReadOnlyList<(Type OldType, Type NewType)> _typePairs;
    private readonly IReadOnlyCollection<Type> _registeredConsumerTypes;
    private readonly ILogger<TranslatorRegistrationValidationHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="TranslatorRegistrationValidationHostedService"/>.
    /// </summary>
    /// <param name="typePairs">The (TOld, TNew) pairs registered via <c>WithVersionTranslator</c>.</param>
    /// <param name="registeredConsumerTypes">The CLR types of all consumers registered in this service.</param>
    /// <param name="logger">Logger used to emit advisory warnings.</param>
    public TranslatorRegistrationValidationHostedService(
        IReadOnlyList<(Type OldType, Type NewType)> typePairs,
        IReadOnlyCollection<Type> registeredConsumerTypes,
        ILogger<TranslatorRegistrationValidationHostedService> logger)
    {
        _typePairs = typePairs;
        _registeredConsumerTypes = registeredConsumerTypes;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var (oldType, newType) in _typePairs)
            TranslatorRegistrationValidator.Validate(oldType, newType, _registeredConsumerTypes, _logger);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
