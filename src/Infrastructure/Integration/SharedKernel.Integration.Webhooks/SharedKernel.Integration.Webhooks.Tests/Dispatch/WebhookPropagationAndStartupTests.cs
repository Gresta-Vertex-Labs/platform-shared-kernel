using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Integration.Webhooks.Options;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// What may leave the platform on a webhook request (the correlation id only, written by the shared propagation
/// helper) and the host misconfiguration that fails at startup instead of at the first delivery.
/// </summary>
public sealed class WebhookPropagationAndStartupTests
{
    private const string CorrelationId = "corr-7f3a9c1e";

    private static WebhookSubscription Subscription(IReadOnlyDictionary<string, string>? headers = null) =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), ["propagation-secret"], [], true, headers);

    private static TestOrderShippedEvent NewEvent() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());

    [Fact]
    public async Task Only_the_correlation_id_leaves_the_platform_never_the_tenant_actor_or_client()
    {
        var subscription = Subscription();
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);
        var context = new SystemRequestContext(["x"], "operator-42", new TenantId(Guid.NewGuid()), CorrelationId);

        using (RequestContextScope.Begin(context))
        {
            await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);
        }

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Headers.GetValues(WellKnownHeaders.CorrelationId).Should().Equal(CorrelationId);
        request.Headers.Contains(WellKnownHeaders.TenantId).Should().BeFalse();
        request.Headers.Contains(WellKnownHeaders.ActorId).Should().BeFalse();
        request.Headers.Contains(WellKnownHeaders.ActorKind).Should().BeFalse();
        request.Headers.Contains(WellKnownHeaders.ClientId).Should().BeFalse();
    }

    [Fact]
    public async Task A_subscription_header_of_the_same_name_wins_over_the_correlation_id()
    {
        var subscription = Subscription(new Dictionary<string, string> { [WellKnownHeaders.CorrelationId] = "subscriber-chosen" });
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);

        using (RequestContextScope.Begin(new SystemRequestContext(["x"], "operator-42", null, CorrelationId)))
        {
            await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);
        }

        handler.Requests.Single().Headers.GetValues(WellKnownHeaders.CorrelationId).Should().Equal("subscriber-chosen");
    }

    [Fact]
    public async Task Without_an_ambient_context_no_correlation_header_is_sent()
    {
        var subscription = Subscription();
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);

        await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Headers.Contains(WellKnownHeaders.CorrelationId).Should().BeFalse();
        request.Headers.Contains(WellKnownHeaders.TenantId).Should().BeFalse();
    }

    [Fact]
    public void Startup_validation_fails_for_encrypted_payloads_without_the_encryption_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddSingleton<IWebhookSubscriptionStore>(new FakeWebhookSubscriptionStore([]));
        services.AddSharedKernelWebhooksForTest(o => o.EncryptPayload = true);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<WebhookDeliveryOptions>>().Value;

        var message = act.Should().Throw<OptionsValidationException>().Which.Message;
        message.Should().Contain("ISymmetricEncryptionService").And.Contain("AddSymmetricEncryption").And.EndWith(".");
    }

    [Fact]
    public void Startup_validation_passes_with_the_encryption_service_or_without_encryption()
    {
        var withService = new ServiceCollection();
        withService.AddLogging();
        withService.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        withService.AddSingleton(Substitute.For<ISymmetricEncryptionService>());
        withService.AddSharedKernelWebhooksForTest(o => o.EncryptPayload = true);
        using var providerWithService = withService.BuildServiceProvider();
        providerWithService.GetRequiredService<IOptions<WebhookDeliveryOptions>>().Value.EncryptPayload.Should().BeTrue();

        var without = new ServiceCollection();
        without.AddLogging();
        without.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        without.AddSharedKernelWebhooksForTest(_ => { });
        using var providerWithout = without.BuildServiceProvider();
        providerWithout.GetRequiredService<IOptions<WebhookDeliveryOptions>>().Value.EncryptPayload.Should().BeFalse();
    }
}

internal static class WebhookRegistrationTestExtensions
{
    /// <summary>Registers the webhook pipeline through the public entry point.</summary>
    internal static IServiceCollection AddSharedKernelWebhooksForTest(this IServiceCollection services, Action<WebhookDeliveryOptions> configure) =>
        SharedKernel.Integration.Webhooks.Extensions.ServiceCollectionExtensions.AddSharedKernelWebhooks(services, configure);
}
