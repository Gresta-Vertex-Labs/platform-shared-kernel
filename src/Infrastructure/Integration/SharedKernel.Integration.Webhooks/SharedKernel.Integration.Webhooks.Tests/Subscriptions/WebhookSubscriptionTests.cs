using FluentAssertions;
using SharedKernel.Integration.Webhooks.Subscriptions;

namespace SharedKernel.Integration.Webhooks.Tests.Subscriptions;

/// <summary>
/// Coverage for the P-425 (<c>Secrets</c>/back-compat <c>Secret</c>) and P-426 (<c>Headers</c>)
/// additions to <see cref="WebhookSubscription"/>.
/// </summary>
public sealed class WebhookSubscriptionTests
{
    [Fact]
    public void Secret_ReturnsNewestSecret()
    {
        var subscription = new WebhookSubscription(
            Guid.NewGuid(),
            new Uri("https://example.test/hook"),
            ["newest", "previous", "oldest"],
            [],
            true);

#pragma warning disable CS0618 // intentionally exercising the back-compat member
        subscription.Secret.Should().Be("newest");
#pragma warning restore CS0618
    }

    [Fact]
    public void ObsoleteConstructor_MapsSingleSecretToOneElementSecretsList()
    {
#pragma warning disable CS0618 // intentionally exercising the back-compat constructor
        var subscription = new WebhookSubscription(
            Guid.NewGuid(),
            new Uri("https://example.test/hook"),
            "single-secret",
            [],
            true);
#pragma warning restore CS0618

        subscription.Secrets.Should().Equal("single-secret");
    }

    [Fact]
    public void Headers_DefaultsToNull()
    {
        var subscription = new WebhookSubscription(
            Guid.NewGuid(),
            new Uri("https://example.test/hook"),
            ["secret"],
            [],
            true);

        subscription.Headers.Should().BeNull();
    }

    [Fact]
    public void Headers_CanBeSuppliedExplicitly()
    {
        var headers = new Dictionary<string, string> { ["X-Partner-Id"] = "partner-123" };

        var subscription = new WebhookSubscription(
            Guid.NewGuid(),
            new Uri("https://example.test/hook"),
            ["secret"],
            [],
            true,
            headers);

        subscription.Headers.Should().BeSameAs(headers);
    }
}
