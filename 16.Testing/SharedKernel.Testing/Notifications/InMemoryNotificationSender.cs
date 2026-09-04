using System.Collections.Concurrent;
using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;

namespace SharedKernel.Testing.Notifications;

/// <summary>
/// In-memory test double for <see cref="INotificationSender"/>. Records every
/// <see cref="SendAsync{TTemplateModel}"/> call for later assertion, returning a
/// caller-configurable result.
/// </summary>
/// <remarks>
/// <para>
/// Constructed with a fixed <see cref="SupportedChannel"/> — a test registers one instance per
/// channel it needs (<see cref="NotificationChannel.Email"/>/<see cref="NotificationChannel.Sms"/>),
/// matching the real keyed-DI-registration convention
/// (<c>AddKeyedScoped&lt;INotificationSender, TSender&gt;(NotificationChannel.X)</c>).
/// </para>
/// <para>
/// Never throws for a "provider failure" — <see cref="SendAsync{TTemplateModel}"/> always returns a
/// <see cref="NotificationDeliveryResult"/>, defaulting to a synthetic success echoing the
/// caller-supplied <c>NotificationDeliveryId</c>, mirroring the real interface's never-throws
/// convention. Records every call — including when no assertion is ever made — into a thread-safe
/// collection; <c>Sent*</c> query members are read-only and never mutate it.
/// </para>
/// </remarks>
public sealed class InMemoryNotificationSender : INotificationSender
{
    private readonly ConcurrentQueue<object> _sent = new();
    private readonly ConcurrentDictionary<Type, Delegate> _resultFactories = new();

    /// <summary>Initialises a new <see cref="InMemoryNotificationSender"/> for <paramref name="supportedChannel"/>.</summary>
    /// <param name="supportedChannel">The single channel this instance serves.</param>
    public InMemoryNotificationSender(NotificationChannel supportedChannel) => SupportedChannel = supportedChannel;

    /// <inheritdoc />
    public NotificationChannel SupportedChannel { get; }

    /// <summary>Every message recorded via <see cref="SendAsync{TTemplateModel}"/>, in call order, boxed as <see cref="object"/>.</summary>
    /// <remarks>
    /// Boxed because <see cref="INotificationSender.SendAsync{TTemplateModel}"/> is generic per
    /// call, not per instance — a single sender may record messages across several distinct
    /// <c>TTemplateModel</c> types. Use <see cref="SentOf{TTemplateModel}"/> to filter to one shape.
    /// </remarks>
    public IReadOnlyList<object> Sent => [.. _sent];

    /// <summary>Every message of type <see cref="NotificationMessage{TTemplateModel}"/> recorded so far, in call order.</summary>
    /// <typeparam name="TTemplateModel">The template model shape to filter to.</typeparam>
    public IReadOnlyList<NotificationMessage<TTemplateModel>> SentOf<TTemplateModel>() =>
        [.. _sent.OfType<NotificationMessage<TTemplateModel>>()];

    /// <summary>
    /// Configures the result <see cref="SendAsync{TTemplateModel}"/> returns for
    /// <typeparamref name="TTemplateModel"/>, overriding the default synthetic success.
    /// </summary>
    /// <typeparam name="TTemplateModel">The template model type to configure a result for.</typeparam>
    /// <param name="resultFactory">Produces the result from the sent message instance.</param>
    public void SetSendResult<TTemplateModel>(Func<NotificationMessage<TTemplateModel>, NotificationDeliveryResult> resultFactory)
    {
        ArgumentNullException.ThrowIfNull(resultFactory);
        _resultFactories[typeof(TTemplateModel)] = resultFactory;
    }

    /// <inheritdoc />
    public Task<NotificationDeliveryResult> SendAsync<TTemplateModel>(
        NotificationMessage<TTemplateModel> message,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        _sent.Enqueue(message);

        if (_resultFactories.TryGetValue(typeof(TTemplateModel), out var factory))
        {
            var typedFactory = (Func<NotificationMessage<TTemplateModel>, NotificationDeliveryResult>)factory;
            return Task.FromResult(typedFactory(message));
        }

        return Task.FromResult(new NotificationDeliveryResult(message.NotificationDeliveryId, true, null, null));
    }

    /// <summary>Returns the first recorded message matching <paramref name="filter"/> (or any, when omitted).</summary>
    /// <typeparam name="TTemplateModel">The expected template model type.</typeparam>
    /// <param name="filter">An optional predicate the matched message must satisfy.</param>
    /// <returns>The matched message.</returns>
    /// <exception cref="InvalidOperationException">No matching message was sent.</exception>
    public NotificationMessage<TTemplateModel> ShouldHaveSent<TTemplateModel>(
        Predicate<NotificationMessage<TTemplateModel>>? filter = null)
    {
        foreach (var candidate in SentOf<TTemplateModel>())
        {
            if (filter is null || filter(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Expected a sent notification of type '{typeof(TTemplateModel).Name}' on channel '{SupportedChannel}' but none was found.");
    }

    /// <summary>Asserts that no message of type <typeparamref name="TTemplateModel"/> was sent.</summary>
    /// <typeparam name="TTemplateModel">The template model type that must not have been sent.</typeparam>
    /// <exception cref="InvalidOperationException">A matching message was sent.</exception>
    public void ShouldNotHaveSent<TTemplateModel>()
    {
        var count = SentOf<TTemplateModel>().Count;
        if (count > 0)
        {
            throw new InvalidOperationException(
                $"Expected no sent notifications of type '{typeof(TTemplateModel).Name}' on channel '{SupportedChannel}' but found {count}.");
        }
    }
}
