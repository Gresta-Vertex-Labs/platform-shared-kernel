using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Presentation.SignalR.Options;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.RateLimiting;

/// <summary>
/// Design D12: the invocation rate limit — one partitioned token-bucket limiter keyed by connection id, off by
/// default, refusing with <c>rate_limit.exceeded: Too many requests.</c> before the hub method runs. B16 regression:
/// no limiter (and so no replenishment timer) per connection.
/// </summary>
public sealed class InvocationRateLimitTests
{
    private const string Refusal = $"{PresentationErrorCodes.RateLimitExceeded}: Too many requests.";

    [Fact]
    public async Task InvocationBeyondThePermitLimit_IsRefused_ForThatConnectionOnly()
    {
        await using var app = await StartAsync(permitLimit: 2);
        await using var first = await app.ConnectAsync(HubPaths.RateLimited);
        await using var second = await app.ConnectAsync(HubPaths.RateLimited);

        (await first.InvokeAsync<string>(nameof(RateLimitedHub.Ping))).Should().Be("pong");
        (await first.InvokeAsync<string>(nameof(RateLimitedHub.Ping))).Should().Be("pong");
        var refusal = await first.InvokeExpectingErrorAsync(nameof(RateLimitedHub.Ping));

        refusal.Should().Be(Refusal);
        (await second.InvokeAsync<string>(nameof(RateLimitedHub.Ping))).Should().Be("pong");
        (await second.InvokeAsync<string>(nameof(RateLimitedHub.Ping))).Should().Be("pong");
    }

    [Fact]
    public async Task RefusedInvocation_NeverRunsTheHubMethod_AndIsLoggedAtWarning()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        await using var app = await StartAsync(permitLimit: 1, loggerFactory: loggerFactory);
        await using var connection = await app.ConnectAsync(HubPaths.RateLimited);
        var counter = app.Services.GetRequiredService<InvocationCounter>();

        await connection.InvokeAsync<string>(nameof(RateLimitedHub.Ping));
        await connection.InvokeExpectingErrorAsync(nameof(RateLimitedHub.Ping));

        counter.Count.Should().Be(1);
        loggerFactory.GetLogger(typeof(HubInvocationRateLimitFilter).FullName!).Records
            .ShouldHaveLogged(new EventId(14101), LogLevel.Warning);
    }

    [Fact]
    public async Task WithoutAPermitLimit_NothingIsLimited_AndNoLimiterExists()
    {
        await using var app = await StartAsync(permitLimit: null);
        await using var connection = await app.ConnectAsync(HubPaths.RateLimited);

        for (var i = 0; i < 25; i++)
        {
            (await connection.InvokeAsync<string>(nameof(RateLimitedHub.Ping))).Should().Be("pong");
        }

        app.Services.GetRequiredService<HubInvocationRateLimitFilter>().Limiter.Should().BeNull();
    }

    [Fact]
    public async Task PermitLimit_BindsFromConfiguration()
    {
        await using var app = await SignalRTestHost.StartAsync(
            app => app.MapHub<RateLimitedHub>(HubPaths.RateLimited),
            configuration: new Dictionary<string, string?>
            {
                ["SharedKernel:Presentation:SignalR:InvocationRateLimit:PermitLimit"] = "1",
                ["SharedKernel:Presentation:SignalR:InvocationRateLimit:Window"] = "00:01:00",
            });
        await using var connection = await app.ConnectAsync(HubPaths.RateLimited);

        await connection.InvokeAsync<string>(nameof(RateLimitedHub.Ping));
        var refusal = await connection.InvokeExpectingErrorAsync(nameof(RateLimitedHub.Ping));

        refusal.Should().Be(Refusal);
    }

    [Fact]
    public async Task B16_OneLimiterServesEveryConnection_AndNothingIsKeptPerConnection()
    {
        await using var app = await StartAsync(permitLimit: 5);
        var connections = new List<HubConnection>();

        try
        {
            var connectionIds = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                var connection = await app.ConnectAsync(HubPaths.RateLimited);
                connections.Add(connection);
                connectionIds.Add(await connection.InvokeAsync<string>(nameof(RateLimitedHub.ConnectionId)));

                // The old filter kept a TokenBucketRateLimiter, with its own replenishment timer, in Context.Items.
                (await connection.InvokeAsync<bool>(nameof(RateLimitedHub.StoresRateLimiterOnConnection))).Should().BeFalse();
            }

            // One limiter for the whole server; each connection is a partition of it that has spent two permits.
            var limiter = app.Services.GetRequiredService<HubInvocationRateLimitFilter>().Limiter;
            limiter.Should().NotBeNull();
            connectionIds.Should().OnlyHaveUniqueItems();

            foreach (var connectionId in connectionIds)
            {
                limiter!.GetStatistics(connectionId)!.CurrentAvailablePermits.Should().Be(3);
            }
        }
        finally
        {
            foreach (var connection in connections)
            {
                await connection.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Limiter_IsDisposedWithTheHost()
    {
        var app = await StartAsync(permitLimit: 5);
        var limiter = app.Services.GetRequiredService<HubInvocationRateLimitFilter>().Limiter!;

        await app.DisposeAsync();

        var acquire = () => limiter.AttemptAcquire("any-connection");
        acquire.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(0, 1, "InvocationRateLimit:PermitLimit")]
    [InlineData(-3, 1, "InvocationRateLimit:PermitLimit")]
    [InlineData(10, 0, "InvocationRateLimit:Window")]
    [InlineData(10, -1, "InvocationRateLimit:Window")]
    public async Task InvalidSettings_StopTheHost(int permitLimit, int windowSeconds, string failingSetting)
    {
        var start = () => SignalRTestHost.StartAsync(
            app => app.MapHub<RateLimitedHub>(HubPaths.RateLimited),
            options =>
            {
                options.InvocationRateLimit.PermitLimit = permitLimit;
                options.InvocationRateLimit.Window = TimeSpan.FromSeconds(windowSeconds);
            });

        var failure = await start.Should().ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>();
        failure.Which.Message.Should().Contain(failingSetting);
    }

    private static Task<WebApplication> StartAsync(int? permitLimit, InMemoryLoggerFactory? loggerFactory = null) =>
        SignalRTestHost.StartAsync(
            app => app.MapHub<RateLimitedHub>(HubPaths.RateLimited),
            options => Configure(options, permitLimit),
            loggerFactory: loggerFactory);

    // A long window keeps a refilled permit from arriving mid-test on a slow machine.
    private static void Configure(SharedKernelSignalROptions options, int? permitLimit)
    {
        options.InvocationRateLimit.PermitLimit = permitLimit;
        options.InvocationRateLimit.Window = TimeSpan.FromMinutes(1);
    }
}
