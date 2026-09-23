using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.SignalR.Options;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Presentation.WebApi.Errors;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Setup;

/// <summary>
/// Design D12/D0: <c>builder.AddSharedKernelSignalR(configure)</c> binds <see cref="SharedKernelSignalROptions"/>
/// from <c>SharedKernel:Presentation:SignalR</c>, runs <c>configure</c> after binding, registers authorization and is
/// safe to call twice.
/// </summary>
public sealed class SignalRSetupTests
{
    [Fact]
    public void AddSharedKernelSignalR_ReturnsSignalRsOwnBuilder_OverTheHostsServices()
    {
        var builder = Host.CreateApplicationBuilder();

        var signalR = builder.AddSharedKernelSignalR();

        signalR.Should().BeAssignableTo<ISignalRServerBuilder>();
        signalR.Services.Should().BeSameAs(builder.Services);
    }

    [Fact]
    public void Options_BindFromTheSection_AndConfigureRunsAfterBinding()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SharedKernel:Presentation:SignalR:InvocationRateLimit:PermitLimit"] = "5",
            ["SharedKernel:Presentation:SignalR:InvocationRateLimit:Window"] = "00:00:10",
        });

        builder.AddSharedKernelSignalR(options => options.InvocationRateLimit.PermitLimit = 3);

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SharedKernelSignalROptions>>().Value;

        SharedKernelSignalROptions.SectionName.Should().Be("SharedKernel:Presentation:SignalR");
        options.InvocationRateLimit.PermitLimit.Should().Be(3);
        options.InvocationRateLimit.Window.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Defaults_LeaveTheRateLimitOff_WithAOneSecondWindow()
    {
        var options = new SharedKernelSignalROptions();

        options.InvocationRateLimit.PermitLimit.Should().BeNull();
        options.InvocationRateLimit.Window.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AddSharedKernelSignalR_RegistersTheSharedKernelAuthorization()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddSharedKernelSignalR();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IAuthorizationPolicyProvider>().GetType().Name
            .Should().Be("SharedKernelAuthorizationPolicyProvider");
    }

    [Fact]
    public async Task CalledTwice_RegistersEachFilterOnce_AndAppliesEveryConfigure()
    {
        await using var app = await SignalRTestHost.StartAsync(
            app => app.MapHub<RateLimitedHub>(HubPaths.RateLimited),
            options => options.InvocationRateLimit.PermitLimit = 2,
            builder => builder.AddSharedKernelSignalR(options => options.InvocationRateLimit.Window = TimeSpan.FromMinutes(1)));
        await using var connection = await app.ConnectAsync(HubPaths.RateLimited);

        // A rate-limit filter registered twice would spend two permits per invocation and refuse the second one.
        (await connection.InvokeAsync<string>(nameof(RateLimitedHub.Ping))).Should().Be("pong");
        (await connection.InvokeAsync<string>(nameof(RateLimitedHub.Ping))).Should().Be("pong");
        (await connection.InvokeExpectingErrorAsync(nameof(RateLimitedHub.Ping)))
            .Should().Be($"{PresentationErrorCodes.RateLimitExceeded}: Too many requests.");

        var options = app.Services.GetRequiredService<IOptions<SharedKernelSignalROptions>>().Value;
        options.InvocationRateLimit.Window.Should().Be(TimeSpan.FromMinutes(1));
    }
}
