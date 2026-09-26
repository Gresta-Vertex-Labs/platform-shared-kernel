using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.MassTransit.AzureServiceBus.DeadLetter;
using SharedKernel.Messaging.MassTransit.Extensions;

namespace SharedKernel.Messaging.MassTransit.AzureServiceBus.Tests.HarnessTests;

/// <summary>
/// DL-07: <see cref="DeadLetterPolicyAdvisoryHostedService"/> — advisory warning fired when
/// <c>WithDeadLetterPolicy()</c> is called while the Azure Service Bus transport is configured.
/// </summary>
public sealed class DeadLetterPolicyAdvisoryTests
{
    // -------------------------------------------------------------------------
    // DL-07: advisory warning fires; Build() does not throw
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HostedService_StartAsync_LogsWarning_DoesNotThrow()
    {
        var capturingLogger = new DlCapturingLoggerOf<DeadLetterPolicyAdvisoryHostedService>();
        var hostedService = new DeadLetterPolicyAdvisoryHostedService(capturingLogger);

        var act = async () => await hostedService.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("the advisory hosted service must never throw");

        capturingLogger.WarningLogged.Should().BeTrue(
            "a Warning must be logged when WithDeadLetterPolicy() has no effect under the " +
            "Azure Service Bus transport");
    }

    [Fact]
    public async Task HostedService_StopAsync_DoesNotThrow()
    {
        var capturingLogger = new DlCapturingLoggerOf<DeadLetterPolicyAdvisoryHostedService>();
        var hostedService = new DeadLetterPolicyAdvisoryHostedService(capturingLogger);

        var act = async () => await hostedService.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void WithDeadLetterPolicy_UnderAzureServiceBusTransport_BuildDoesNotThrow()
    {
        var services = new ServiceCollection();
        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseAzureServiceBus(o => o.ConnectionString =
                "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake=")
            .WithDeadLetterPolicy()
            .Build();

        act.Should().NotThrow(
            "WithDeadLetterPolicy() under Azure Service Bus must log an advisory warning at " +
            "startup rather than throwing at Build() time");
    }

    [Fact]
    public void WithDeadLetterPolicy_UnderAzureServiceBusTransport_RegistersAdvisoryHostedService()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseAzureServiceBus(o => o.ConnectionString =
                "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake=")
            .WithDeadLetterPolicy()
            .Build();

        services.Should().Contain(
            d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(DeadLetterPolicyAdvisoryHostedService),
            "Build() must register the advisory hosted service when WithDeadLetterPolicy() is " +
            "combined with the Azure Service Bus transport");
    }

    [Fact]
    public void WithDeadLetterPolicy_UnderRabbitMqTransport_DoesNotRegisterAdvisoryHostedService()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .WithDeadLetterPolicy()
            .Build();

        services.Should().NotContain(
            d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(DeadLetterPolicyAdvisoryHostedService),
            "the advisory hosted service is only relevant under the Azure Service Bus transport — " +
            "RabbitMQ genuinely honors WithDeadLetterPolicy()");
    }

    [Fact]
    public void WithoutDeadLetterPolicy_UnderAzureServiceBusTransport_DoesNotRegisterAdvisoryHostedService()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelMessaging(o => o.ServiceName = "test-service")
            .UseAzureServiceBus(o => o.ConnectionString =
                "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake=")
            .Build();

        services.Should().NotContain(
            d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(DeadLetterPolicyAdvisoryHostedService),
            "the advisory hosted service must only be registered when WithDeadLetterPolicy() was " +
            "actually called");
    }
}

// ---------------------------------------------------------------------------
// Capturing logger — mirrors VtCapturingLoggerOf<T> in VersionTranslationTests.cs
// ---------------------------------------------------------------------------

/// <summary>Generic capturing <see cref="ILogger{TCategoryName}"/> that records whether a Warning was logged.</summary>
internal sealed class DlCapturingLoggerOf<T> : ILogger<T>
{
    public bool WarningLogged { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
            WarningLogged = true;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
