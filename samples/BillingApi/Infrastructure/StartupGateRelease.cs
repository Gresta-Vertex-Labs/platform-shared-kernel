using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.ServiceDefaults.Probes;

namespace BillingApi.Infrastructure;

/// <summary>
/// Opens the startup gate once the startup migrations and seeders have finished, so <c>/health/ready</c> stays 503
/// while a new replica is still migrating.
/// </summary>
public sealed class StartupGateRelease(IPersistenceStartup persistence, StartupGate gate) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await persistence.WaitAsync(stoppingToken);
        gate.MarkReady();
    }
}
