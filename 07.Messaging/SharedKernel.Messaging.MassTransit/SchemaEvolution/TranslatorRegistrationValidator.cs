using MassTransit;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Messaging.MassTransit.SchemaEvolution;

/// <summary>
/// Advisory validator invoked at host startup for each
/// <c>WithVersionTranslator&lt;TOld, TNew, TTranslator&gt;()</c> registration captured by
/// <c>MessagingBusBuilder.Build()</c>.
/// </summary>
/// <remarks>
/// <para>
/// For each registration, checks whether any consumer for the new schema type has been
/// registered in the same service (via the consumer types passed to
/// <c>AddConsumer&lt;TConsumer&gt;()</c>, <c>AddConsumer&lt;TConsumer, TDefinition&gt;()</c>, or
/// <c>AddBatchConsumer&lt;TConsumer&gt;()</c>). If no consumer for the new schema type is found,
/// logs a <see cref="LogLevel.Warning"/> via <see cref="ILogger"/>.
/// </para>
/// <para>
/// This is advisory only — it does <strong>not</strong> throw. The consumer for the new schema
/// type may be registered in a separate service. Invoked from
/// <see cref="TranslatorRegistrationValidationHostedService"/>, which runs once at host startup
/// using the application's configured logging providers.
/// </para>
/// </remarks>
internal static partial class TranslatorRegistrationValidator
{
    /// <summary>
    /// Validates a single <c>WithVersionTranslator&lt;TOld, TNew, TTranslator&gt;()</c> registration
    /// against the set of message types consumed by registered consumer types.
    /// </summary>
    /// <param name="oldType">The legacy schema type (<c>TOld</c>).</param>
    /// <param name="newType">The current schema type (<c>TNew</c>).</param>
    /// <param name="registeredConsumerTypes">
    /// The CLR types of all consumers registered via <c>AddConsumer</c> / <c>AddBatchConsumer</c>
    /// in this service.
    /// </param>
    /// <param name="logger">Logger used to emit the advisory warning.</param>
    public static void Validate(
        Type oldType,
        Type newType,
        IReadOnlyCollection<Type> registeredConsumerTypes,
        ILogger logger)
    {
        if (!HasConsumerFor(newType, registeredConsumerTypes))
            LogNoConsumerForNewSchema(logger, oldType.Name, newType.Name);
    }

    /// <summary>
    /// Determines whether any of the given consumer types implements
    /// <c>IConsumer&lt;newType&gt;</c> or <c>IConsumer&lt;Batch&lt;newType&gt;&gt;</c>.
    /// </summary>
    private static bool HasConsumerFor(Type newType, IReadOnlyCollection<Type> consumerTypes)
    {
        var directConsumer = typeof(IConsumer<>).MakeGenericType(newType);
        var batchConsumer = typeof(IConsumer<>).MakeGenericType(typeof(Batch<>).MakeGenericType(newType));

        foreach (var consumerType in consumerTypes)
        {
            foreach (var iface in consumerType.GetInterfaces())
            {
                if (iface == directConsumer || iface == batchConsumer)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Logs an advisory warning when no consumer for the new schema type was found in this service.
    /// </summary>
    [LoggerMessage(
        EventId = 7009,
        Level = LogLevel.Warning,
        Message = "WithVersionTranslator registered a translation from {OldType} to {NewType}, but no " +
            "consumer for the new schema type was found in this service. This is advisory only — " +
            "the consumer may be registered in a separate service.")]
    private static partial void LogNoConsumerForNewSchema(ILogger logger, string oldType, string newType);
}
