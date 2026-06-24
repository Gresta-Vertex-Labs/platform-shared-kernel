using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Testing.Messaging;

/// <summary>
/// Static factory configuring a <see cref="MassTransit.Testing.ITestHarness"/> with platform
/// defaults for tests that need real MassTransit pipeline fidelity (consumer definitions, retry,
/// sagas, routing slips) beyond what <see cref="InMemoryMessageBus"/>/<see cref="InMemoryEventPublisher"/>
/// can simulate.
/// </summary>
/// <remarks>
/// This is the only type in <c>Messaging/</c> permitted to carry a
/// <c>SharedKernel.Messaging.MassTransit</c> / <c>MassTransit.Testing</c> reference —
/// <see cref="InMemoryMessageBus"/> and <see cref="InMemoryEventPublisher"/> remain reference-isolated
/// to <c>SharedKernel.Messaging.Abstractions</c> only. Permitted because <c>16.Testing</c> is never
/// shipped inside a production artifact.
/// </remarks>
public static class TestHarnessFactory
{
    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> wired with <c>AddMassTransitTestHarness</c>,
    /// applying platform defaults (<see cref="KebabCaseEndpointNameFormatter"/>) and any
    /// consumer/saga/activity registrations supplied via <paramref name="configure"/>.
    /// </summary>
    /// <param name="serviceName">
    /// The logical service name. Currently informational only — <c>ITestHarness</c> does not use a
    /// per-call service name, but this parameter keeps parity with production
    /// <c>AddSharedKernelMessaging(o => o.ServiceName = ...)</c> call sites.
    /// </param>
    /// <param name="configure">
    /// Optional callback to register consumers (typically <c>ConsumerBase&lt;T&gt;</c> subclasses),
    /// sagas, or routing-slip activities on the harness's <see cref="IBusRegistrationConfigurator"/>.
    /// </param>
    /// <returns>
    /// A started <see cref="MassTransit.Testing.ITestHarness"/>. Callers are responsible for calling
    /// <see cref="MassTransit.Testing.ITestHarness.Stop"/> (or disposing the owning
    /// <see cref="ServiceProvider"/>) when the test completes.
    /// </returns>
    public static async Task<ITestHarness> CreateAsync(
        string serviceName,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.SetKebabCaseEndpointNameFormatter();
                configure?.Invoke(cfg);
            })
            .BuildServiceProvider(validateScopes: true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start().ConfigureAwait(false);
        return harness;
    }
}
