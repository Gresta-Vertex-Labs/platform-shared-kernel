using System.Collections.Concurrent;
using SharedKernel.Contracts.Events;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Subscriptions;

namespace SharedKernel.Testing.Integration;

/// <summary>
/// In-memory test double for <see cref="IWebhookDispatcher"/>. Records every
/// <see cref="DispatchAsync{TEvent}"/>/<see cref="DispatchToSubscriptionAsync{TEvent}"/>/
/// <see cref="SendTestDeliveryAsync"/> call for later assertion, returning a caller-configurable
/// result for each.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This fake performs no real subscription-store lookup, HTTP delivery, or HMAC signing.</strong>
/// It is a recorder+configurable-result double for APPLICATION-layer tests asserting "did my command
/// handler correctly trigger a webhook dispatch," not a fan-out/routing-fidelity double for testing
/// <c>WebhookDispatcher</c> itself — that real routing logic is already covered by
/// <c>15.Integration</c>'s own <c>WebhookDispatcherFanOutTests.cs</c>.
/// </para>
/// <para>
/// Each of the three interface members has its own independent recording list and its own
/// independent configurable-result mechanism — this fake proves each member's own observable
/// contract, not production's internal call graph (production's <c>SendTestDeliveryAsync</c>
/// internally constructs a <c>WebhookPingEvent</c> and delegates to
/// <see cref="DispatchToSubscriptionAsync{TEvent}"/>; this fake deliberately does not reproduce that
/// delegation, since a fake's job is per-member recording, not call-graph fidelity).
/// </para>
/// <para>
/// Records every call — including when no assertion is ever made — into thread-safe collections.
/// The <c>Should*</c> assertion helpers are read-only queries over those collections and never
/// mutate them.
/// </para>
/// </remarks>
public sealed class InMemoryWebhookDispatcher : IWebhookDispatcher
{
    private readonly ConcurrentQueue<object> _dispatched = new();
    private readonly ConcurrentQueue<(Guid SubscriptionId, object Event)> _dispatchedTo = new();
    private readonly ConcurrentQueue<WebhookSubscription> _testDeliveries = new();
    private readonly ConcurrentDictionary<Type, Delegate> _dispatchResultFactories = new();
    private readonly ConcurrentDictionary<Guid, WebhookDeliveryResult> _dispatchToResults = new();
    private readonly ConcurrentDictionary<Guid, WebhookDeliveryResult> _testDeliveryResults = new();

    /// <inheritdoc />
    /// <remarks>
    /// Records <paramref name="integrationEvent"/> and returns the result list configured via
    /// <see cref="SetDispatchResult{TEvent}"/> for <typeparamref name="TEvent"/>, or an empty list
    /// when unconfigured — the honest default for a fake that performs no real subscription-store
    /// lookup, mirroring "zero matching subscriptions" rather than fabricating a fan-out this type
    /// cannot see.
    /// </remarks>
    public Task<IReadOnlyList<WebhookDeliveryResult>> DispatchAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        _dispatched.Enqueue(integrationEvent);

        if (_dispatchResultFactories.TryGetValue(typeof(TEvent), out var factory))
        {
            var typedFactory = (Func<TEvent, IReadOnlyList<WebhookDeliveryResult>>)factory;
            return Task.FromResult(typedFactory(integrationEvent));
        }

        return Task.FromResult<IReadOnlyList<WebhookDeliveryResult>>([]);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Records the <c>(SubscriptionId, event)</c> pair and returns the result configured via
    /// <see cref="SetDispatchResult(Guid, WebhookDeliveryResult)"/> for <paramref name="subscription"/>'s
    /// <see cref="WebhookSubscription.SubscriptionId"/>, or a synthetic 2xx success when unconfigured
    /// — a deliberate divergence from <c>InMemoryMessageBus.RequestAsync</c>'s "throw when
    /// unconfigured" shape, since <see cref="IWebhookDispatcher"/>'s own documented contract states a
    /// delivery failure is ALWAYS expressed as the returned record, never a thrown exception.
    /// </remarks>
    public Task<WebhookDeliveryResult> DispatchToSubscriptionAsync<TEvent>(
        WebhookSubscription subscription,
        TEvent integrationEvent,
        CancellationToken ct)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(integrationEvent);
        _dispatchedTo.Enqueue((subscription.SubscriptionId, integrationEvent));

        var result = _dispatchToResults.TryGetValue(subscription.SubscriptionId, out var configured)
            ? configured
            : new WebhookDeliveryResult(subscription.SubscriptionId, Guid.NewGuid(), true, 200, 1, null);

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Records <paramref name="subscription"/> into <see cref="TestDeliveries"/> and returns the
    /// result configured via <see cref="SetTestDeliveryResult"/>, or a synthetic 2xx success when
    /// unconfigured. Tracked independently of <see cref="DispatchedTo"/> — a ping delivery is
    /// observably distinct from a real event delivery.
    /// </remarks>
    public Task<WebhookDeliveryResult> SendTestDeliveryAsync(WebhookSubscription subscription, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _testDeliveries.Enqueue(subscription);

        var result = _testDeliveryResults.TryGetValue(subscription.SubscriptionId, out var configured)
            ? configured
            : new WebhookDeliveryResult(subscription.SubscriptionId, Guid.NewGuid(), true, 200, 1, null);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Configures the result <see cref="DispatchAsync{TEvent}"/> returns for
    /// <typeparamref name="TEvent"/>, overriding the default empty list.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type to configure a result for.</typeparam>
    /// <param name="resultFactory">Produces the result list from the dispatched event instance.</param>
    public void SetDispatchResult<TEvent>(Func<TEvent, IReadOnlyList<WebhookDeliveryResult>> resultFactory)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(resultFactory);
        _dispatchResultFactories[typeof(TEvent)] = resultFactory;
    }

    /// <summary>
    /// Configures the result <see cref="DispatchToSubscriptionAsync{TEvent}"/> returns for the given
    /// subscription id, overriding the default synthetic 2xx success.
    /// </summary>
    /// <param name="subscriptionId">The subscription id to configure a result for.</param>
    /// <param name="result">The result to return for that subscription id.</param>
    public void SetDispatchResult(Guid subscriptionId, WebhookDeliveryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _dispatchToResults[subscriptionId] = result;
    }

    /// <summary>
    /// Configures the result <see cref="SendTestDeliveryAsync"/> returns for the given subscription
    /// id, overriding the default synthetic 2xx success.
    /// </summary>
    /// <param name="subscriptionId">The subscription id to configure a result for.</param>
    /// <param name="result">The result to return for that subscription id.</param>
    public void SetTestDeliveryResult(Guid subscriptionId, WebhookDeliveryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _testDeliveryResults[subscriptionId] = result;
    }

    /// <summary>Every event instance recorded via <see cref="DispatchAsync{TEvent}"/>, in call order.</summary>
    public IReadOnlyList<object> Dispatched => [.. _dispatched];

    /// <summary>
    /// Every <c>(SubscriptionId, event)</c> pair recorded via <see cref="DispatchToSubscriptionAsync{TEvent}"/>,
    /// in call order.
    /// </summary>
    public IReadOnlyList<(Guid SubscriptionId, object Event)> DispatchedTo => [.. _dispatchedTo];

    /// <summary>Every subscription recorded via <see cref="SendTestDeliveryAsync"/>, in call order.</summary>
    public IReadOnlyList<WebhookSubscription> TestDeliveries => [.. _testDeliveries];

    /// <summary>Returns the first recorded <see cref="DispatchAsync{TEvent}"/> event of type <typeparamref name="TEvent"/>.</summary>
    /// <typeparam name="TEvent">The expected event type.</typeparam>
    /// <returns>The matched event.</returns>
    /// <exception cref="InvalidOperationException">No matching event was dispatched.</exception>
    public TEvent ShouldHaveDispatched<TEvent>() where TEvent : IIntegrationEvent
    {
        foreach (var candidate in _dispatched)
        {
            if (candidate is TEvent typed)
            {
                return typed;
            }
        }

        throw new InvalidOperationException(
            $"Expected a dispatched event of type '{typeof(TEvent).Name}' but none was found.");
    }

    /// <summary>Asserts that no event of type <typeparamref name="TEvent"/> was dispatched.</summary>
    /// <typeparam name="TEvent">The event type that must not have been dispatched.</typeparam>
    /// <exception cref="InvalidOperationException">A matching event was dispatched.</exception>
    public void ShouldNotHaveDispatched<TEvent>() where TEvent : IIntegrationEvent
    {
        var count = _dispatched.Count(candidate => candidate is TEvent);
        if (count > 0)
        {
            throw new InvalidOperationException(
                $"Expected no dispatched events of type '{typeof(TEvent).Name}' but found {count}.");
        }
    }

    /// <summary>Asserts that <see cref="DispatchToSubscriptionAsync{TEvent}"/> was called for the given subscription id.</summary>
    /// <param name="subscriptionId">The subscription id expected to have received a dispatch.</param>
    /// <exception cref="InvalidOperationException">No matching dispatch was recorded.</exception>
    public void ShouldHaveDispatchedTo(Guid subscriptionId)
    {
        var found = _dispatchedTo.Any(entry => entry.SubscriptionId == subscriptionId);
        if (!found)
        {
            throw new InvalidOperationException(
                $"Expected a dispatch to subscription '{subscriptionId}' but none was found.");
        }
    }

    /// <summary>Asserts that <see cref="SendTestDeliveryAsync"/> was called for the given subscription id.</summary>
    /// <param name="subscriptionId">The subscription id expected to have received a test delivery.</param>
    /// <exception cref="InvalidOperationException">No matching test delivery was recorded.</exception>
    public void ShouldHaveSentTestDelivery(Guid subscriptionId)
    {
        var found = _testDeliveries.Any(subscription => subscription.SubscriptionId == subscriptionId);
        if (!found)
        {
            throw new InvalidOperationException(
                $"Expected a test delivery to subscription '{subscriptionId}' but none was found.");
        }
    }
}
