using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.PubSub.Diagnostics;
using SharedKernel.Caching.Redis.PubSub.Extensions;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests;

/// <summary>
/// The channel service's log statements against a real Redis: ids stay in the package sub-block and keep their
/// numbers, templates follow the conventions, and every caught failure passes its exception.
/// </summary>
public sealed class PubSubLoggingTests
{
    private const int SubBlock = 5;

    [Fact]
    public void EventIds_KeepTheirNumbers_InsideThePubSubSubBlock()
    {
        Assert.Equal(2500, PubSubLoggingEventIds.HandlerFailed);
        Assert.Equal(2501, PubSubLoggingEventIds.MessageNotDeserialized);
        Assert.Equal(2502, PubSubLoggingEventIds.UnsubscribeFailed);
        Assert.All(
            typeof(PubSubLoggingEventIds).GetFields().Where(f => f.IsLiteral && f.Name != nameof(PubSubLoggingEventIds.Base)),
            field => Assert.True(
                LoggingEventIdRanges.IsInSubBlock((int)field.GetRawConstantValue()!, LoggingEventIdRanges.Caching, SubBlock),
                field.Name));
    }

    [Fact]
    public async Task HandlerFailureAndMalformedMessage_AreLoggedWithTheirExceptionAndConventionalTemplates()
    {
        await using var container = new RedisBuilder("redis:7-alpine").Build();
        await container.StartAsync();
        await using var provider = BuildProvider(container.GetConnectionString(), out var loggers);
        var channels = provider.GetRequiredService<IRedisChannelService>();
        var channel = RedisFixture.NewChannel("logging");

        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var textSubscription = await channels.SubscribeAsync(
            channel,
            (message, _) => message == "boom" ? throw new InvalidOperationException("handler failed") : ValueTask.CompletedTask);
        await using var typedSubscription = await channels.SubscribeAsync(
            channel,
            TestJsonContext.Default.TestMessage,
            (_, _) =>
            {
                handled.TrySetResult();
                return ValueTask.CompletedTask;
            });

        await channels.PublishAsync(channel, "boom");
        await channels.PublishAsync(channel, "{\"Kind\":\"ok\",\"Sequence\":1}");
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The two subscriptions dispatch independently, so the typed handler finishing says nothing about whether the
        // text handler has thrown and been logged yet: wait for both statements instead of assuming an order.
        var records = await WaitForRecordsAsync(
            loggers,
            r => r.Any(x => x.EventId.Id == PubSubLoggingEventIds.HandlerFailed)
                && r.Any(x => x.EventId.Id == PubSubLoggingEventIds.MessageNotDeserialized));
        var failed = records.ShouldHaveLoggedWithException(PubSubLoggingEventIds.HandlerFailed, typeof(InvalidOperationException));
        Assert.Equal(LogLevel.Error, failed.LogLevel);
        records.ShouldHaveLoggedTemplate(
            PubSubLoggingEventIds.HandlerFailed,
            "Handler for Redis channel '{Channel}' failed, and the subscription continues with the next message.");
        var skipped = records.ShouldHaveLoggedWithException(PubSubLoggingEventIds.MessageNotDeserialized, typeof(System.Text.Json.JsonException));
        Assert.Equal(LogLevel.Warning, skipped.LogLevel);
        records.ShouldHaveLoggedTemplate(
            PubSubLoggingEventIds.MessageNotDeserialized,
            "A message on Redis channel '{Channel}' is not a valid '{MessageType}', so it was skipped.");
        Assert.True(skipped.TryGetProperty("MessageType", out var messageType));
        Assert.Equal(nameof(TestMessage), messageType);
        records.Where(r => r.EventId.Id is >= 2500 and <= 2599).ToList().ShouldFollowLogConvention()
            .ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Caching, SubBlock);
        loggers.ShouldHaveCategory(typeof(RedisChannelService));
    }

    // StackExchange.Redis completes an unsubscribe on a dropped connection without throwing, so the catch in
    // RedisChannelSubscription.DisposeAsync cannot be driven against a real Redis; the statement's signature is pinned
    // instead, so a caught failure always has somewhere to put its exception.
    [Fact]
    public void EveryFailureStatement_TakesTheExceptionAsAParameter()
    {
        foreach (var name in new[] { nameof(PubSubLog.HandlerFailed), nameof(PubSubLog.MessageNotDeserialized), nameof(PubSubLog.UnsubscribeFailed) })
        {
            var method = typeof(PubSubLog).GetMethod(name)!;
            Assert.Contains(method.GetParameters(), p => p.ParameterType == typeof(Exception));
            Assert.Equal(typeof(Exception), method.GetParameters()[^1].ParameterType);
        }
    }

    private static async Task<List<LogRecord>> WaitForRecordsAsync(
        InMemoryLoggerFactory loggers,
        Func<List<LogRecord>, bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var records = loggers.Loggers.Values.SelectMany(l => l.Records).ToList();
            if (condition(records) || DateTime.UtcNow >= deadline)
            {
                return records;
            }

            await Task.Delay(25);
        }
    }

    private static ServiceProvider BuildProvider(string connectionString, out InMemoryLoggerFactory loggers)
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services
            .AddRedisConnection(o =>
            {
                o.ConnectionString = connectionString;
                o.ConnectTimeout = TimeSpan.FromMilliseconds(500);
                o.CommandTimeout = TimeSpan.FromMilliseconds(500);
            })
            .AddRedisChannelService();
        var provider = services.BuildServiceProvider();
        loggers = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        return provider;
    }
}
