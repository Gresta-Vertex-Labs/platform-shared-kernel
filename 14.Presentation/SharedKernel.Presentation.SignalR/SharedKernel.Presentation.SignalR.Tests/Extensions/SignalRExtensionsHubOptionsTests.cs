using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.SignalR.Extensions;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Extensions;

/// <summary>
/// Tests for <see cref="SignalRExtensions.AddSharedKernelSignalR"/>'s conservative default
/// <see cref="HubOptions"/> resource-exhaustion values (WO-062, P-409).
/// </summary>
public class SignalRExtensionsHubOptionsTests
{
    [Fact]
    public void AddSharedKernelSignalR_NoConfigureCallback_HubOptionsMatchDocumentedDefaults()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelSignalR();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HubOptions>>().Value;

        options.MaximumReceiveMessageSize.Should().Be(32 * 1024);
        options.MaximumParallelInvocationsPerClient.Should().Be(1);
        options.ClientTimeoutInterval.Should().Be(TimeSpan.FromSeconds(30));
        options.KeepAliveInterval.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void AddSharedKernelSignalR_ConfigureCallback_OverridesEveryDefaultIndependently()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 64 * 1024;
            options.MaximumParallelInvocationsPerClient = 4;
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
            options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HubOptions>>().Value;

        options.MaximumReceiveMessageSize.Should().Be(64 * 1024);
        options.MaximumParallelInvocationsPerClient.Should().Be(4);
        options.ClientTimeoutInterval.Should().Be(TimeSpan.FromSeconds(60));
        options.KeepAliveInterval.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void AddSharedKernelSignalR_ConfigureCallback_CanRaiseASingleDefault_OthersUnaffected()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelSignalR(options => options.MaximumReceiveMessageSize = 128 * 1024);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HubOptions>>().Value;

        options.MaximumReceiveMessageSize.Should().Be(128 * 1024);
        options.MaximumParallelInvocationsPerClient.Should().Be(1);
        options.ClientTimeoutInterval.Should().Be(TimeSpan.FromSeconds(30));
        options.KeepAliveInterval.Should().Be(TimeSpan.FromSeconds(15));
    }
}
