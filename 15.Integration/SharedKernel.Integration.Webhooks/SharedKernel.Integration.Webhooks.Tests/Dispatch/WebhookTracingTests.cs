using System.Diagnostics;
using System.Net;
using FluentAssertions;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// Coverage for P-424: <see cref="WebhookIntegrationActivitySource"/> emits spans for both a
/// successful and a failed delivery, with the documented tags present and
/// <c>WebhookSubscription.Url</c>/signing secret never present as a tag key or value.
/// </summary>
/// <remarks>
/// <see cref="ActivitySource"/>'s listener registration is process-wide, and xUnit runs different
/// test classes in parallel by default — every assertion below therefore isolates "this test's own"
/// spans by the globally-unique <see cref="WebhookSubscription.SubscriptionId"/> tag (never by
/// operation name alone), so a concurrently-running dispatch from another test class cannot produce
/// a false positive/negative.
/// </remarks>
public sealed class WebhookTracingTests
{
    private static WebhookSubscription Subscription(string secret = "super-secret-signing-key") =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), [secret], [], true);

    [Fact]
    public async Task DispatchToSubscriptionAsync_SuccessfulDelivery_EmitsSpanWithExpectedTags()
    {
        using var activities = new ActivityCollector();

        var secret = "super-secret-signing-key";
        var subscription = Subscription(secret);
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        var span = FindSubscriptionSpan(activities, subscription.SubscriptionId);

        span.GetTagItem(WebhookActivityTags.EventType).Should().Be(nameof(TestOrderShippedEvent));
        span.GetTagItem(WebhookActivityTags.Outcome).Should().Be("success");
        span.GetTagItem(WebhookActivityTags.AttemptCount).Should().Be(1);

        AssertNoSensitiveData(activities.Captured, subscription.Url, secret);
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_FailedDelivery_EmitsSpanWithFailureOutcome()
    {
        using var activities = new ActivityCollector();

        var secret = "another-signing-secret";
        var subscription = Subscription(secret);
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        var span = FindSubscriptionSpan(activities, subscription.SubscriptionId);

        span.GetTagItem(WebhookActivityTags.Outcome).Should().Be("failure");

        AssertNoSensitiveData(activities.Captured, subscription.Url, secret);
    }

    [Fact]
    public async Task DispatchAsync_FanOut_EmitsParentSpanWithSubscriptionCount()
    {
        using var activities = new ActivityCollector();

        var subscription1 = Subscription("secret-one");
        var subscription2 = Subscription("secret-two");
        var store = new FakeWebhookSubscriptionStore([subscription1, subscription2]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        await harness.Dispatcher.DispatchAsync(
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()), CancellationToken.None);

        var childSpan1 = FindSubscriptionSpan(activities, subscription1.SubscriptionId);
        var childSpan2 = FindSubscriptionSpan(activities, subscription2.SubscriptionId);

        childSpan1.ParentSpanId.Should().Be(childSpan2.ParentSpanId, "both deliveries belong to the same fan-out");

        var parentSpan = activities.Captured.Single(a => a.SpanId == childSpan1.ParentSpanId);

        parentSpan.OperationName.Should().Be("WebhookDispatcher.Dispatch");
        parentSpan.GetTagItem(WebhookActivityTags.SubscriptionCount).Should().Be(2);
        parentSpan.GetTagItem(WebhookActivityTags.EventType).Should().Be(nameof(TestOrderShippedEvent));
    }

    private static Activity FindSubscriptionSpan(ActivityCollector activities, Guid subscriptionId)
    {
        var matches = activities.Captured
            .Where(a => a.OperationName == "WebhookDispatcher.DispatchToSubscription")
            .Where(a => a.GetTagItem(WebhookActivityTags.SubscriptionId) is Guid tagged && tagged == subscriptionId)
            .ToList();

        matches.Should().ContainSingle();
        return matches[0];
    }

    private static void AssertNoSensitiveData(IReadOnlyList<Activity> activities, Uri url, string secret)
    {
        foreach (var activity in activities)
        {
            foreach (var tag in activity.TagObjects)
            {
                tag.Key.Should().NotBe("url", "the subscription URL must never be tagged");
                tag.Value?.ToString().Should().NotContain(url.ToString());
                tag.Value?.ToString().Should().NotContain(secret);
            }
        }
    }

    /// <summary>Captures every <see cref="Activity"/> emitted on <see cref="WebhookIntegrationActivitySource.Name"/> for the lifetime of the instance.</summary>
    private sealed class ActivityCollector : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly List<Activity> _captured = [];

        public ActivityCollector()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == WebhookIntegrationActivitySource.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (_captured)
                    {
                        _captured.Add(activity);
                    }
                },
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyList<Activity> Captured
        {
            get
            {
                lock (_captured)
                {
                    return [.. _captured];
                }
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
