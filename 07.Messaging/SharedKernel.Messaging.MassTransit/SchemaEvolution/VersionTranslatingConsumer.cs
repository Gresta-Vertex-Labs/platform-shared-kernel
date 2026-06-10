using MassTransit;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.Abstractions.SchemaEvolution;

namespace SharedKernel.Messaging.MassTransit.SchemaEvolution;

/// <summary>
/// Internal MassTransit consumer that receives messages of the legacy schema
/// <typeparamref name="TOld"/>, projects them to <typeparamref name="TNew"/> via the registered
/// <see cref="IMessageVersionTranslator{TOld, TNew}"/>, and republishes the result so that
/// consumers registered for <typeparamref name="TNew"/> receive the translated payload.
/// </summary>
/// <typeparam name="TOld">The legacy message schema type.</typeparam>
/// <typeparam name="TNew">The current message schema type.</typeparam>
/// <remarks>
/// Registered by <c>MessagingBusBuilder.WithVersionTranslator&lt;TOld, TNew, TTranslator&gt;()</c> —
/// never register directly. <see cref="IMessageVersionTranslator{TOld, TNew}.Translate"/> is invoked
/// synchronously; the resulting <typeparamref name="TNew"/> message is published via
/// <c>ConsumeContext.Publish{T}(T, CancellationToken)</c>, preserving the original
/// <c>ConsumeContext.CorrelationId</c>.
/// </remarks>
internal sealed class VersionTranslatingConsumer<TOld, TNew> : IConsumer<TOld>
    where TOld : class
    where TNew : class
{
    private static readonly Action<ILogger, string, string, Exception?> LogTranslating =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(3, "VersionTranslating"),
            "Translating message {OldType} to {NewType}.");

    private readonly IMessageVersionTranslator<TOld, TNew> _translator;
    private readonly ILogger<VersionTranslatingConsumer<TOld, TNew>> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="VersionTranslatingConsumer{TOld, TNew}"/>.
    /// </summary>
    /// <param name="translator">The registered translator for <typeparamref name="TOld"/> → <typeparamref name="TNew"/>.</param>
    /// <param name="logger">Logger for structured diagnostics.</param>
    public VersionTranslatingConsumer(
        IMessageVersionTranslator<TOld, TNew> translator,
        ILogger<VersionTranslatingConsumer<TOld, TNew>> logger)
    {
        _translator = translator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task Consume(ConsumeContext<TOld> context)
    {
        LogTranslating(_logger, typeof(TOld).Name, typeof(TNew).Name, null);

        // VT-01: Translate is a synchronous pure projection — no I/O, no side effects.
        var translated = _translator.Translate(context.Message);

        await context.Publish(translated, context.CancellationToken).ConfigureAwait(false);
    }
}
