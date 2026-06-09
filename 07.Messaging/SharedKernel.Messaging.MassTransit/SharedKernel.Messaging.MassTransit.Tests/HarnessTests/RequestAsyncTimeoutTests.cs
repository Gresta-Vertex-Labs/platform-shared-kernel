using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// T-10: RequestAsync timeout test.
/// No responder registered; cancellation token expires; does not hang.
/// </summary>
public sealed class RequestAsyncTimeoutTests
{
    [Fact]
    public async Task RequestAsync_NoResponder_ThrowsWhenTokenExpires()
    {
        await using var provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // Very short timeout to avoid hanging the test suite
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var act = async () => await bus.RequestAsync<PingRequest, PongResponse>(
            new PingRequest("ping"),
            cts.Token);

        // When no responder is registered, MassTransit throws OperationCanceledException or
        // MassTransit.RequestException (wrapping a timeout) when the token expires
        await act.Should().ThrowAsync<Exception>(
            "RequestAsync must not hang when no responder is registered and the token expires");

        await harness.Stop();
    }

    [Fact]
    public async Task RequestAsync_NoResponder_CompletesWithinReasonableTime()
    {
        await using var provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await bus.RequestAsync<PingRequest, PongResponse>(new PingRequest("ping"), cts.Token);
        }
        catch
        {
            // Expected — timeout or cancellation
        }

        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
            "RequestAsync must not hang after cancellation token expires");

        await harness.Stop();
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddMassTransitTestHarness();
        services.Configure<MessagingOptions>(o => o.ServiceName = "request-test-service");

        // Register routing dependencies that MassTransitMessageBus now requires.
        services.AddSingleton<IReadOnlyDictionary<Type, string>>(
            new System.Collections.ObjectModel.ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()));
        services.AddScoped<ConventionSendEndpointResolver>();
        services.AddScoped<IMessageBus, MassTransitMessageBus>();

        return services.BuildServiceProvider(true);
    }
}

file sealed record PingRequest(string Data);

file sealed record PongResponse(string Reply);
