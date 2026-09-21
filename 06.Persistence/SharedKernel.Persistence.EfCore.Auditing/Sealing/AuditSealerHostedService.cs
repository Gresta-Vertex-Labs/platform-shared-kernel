using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Auditing.Checkpoints;
using SharedKernel.Persistence.EfCore.Auditing.Diagnostics;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Auditing.Sealing;

/// <summary>
/// The background sealer: every <see cref="AuditSealerOptions.Interval"/> it seals until drained (only the
/// instance holding the advisory lock does work), and every <see cref="AuditSealerOptions.CheckpointInterval"/>
/// it checkpoints each chain that moved since the previous emission.
/// </summary>
internal sealed class AuditSealerHostedService(
    AuditSealingEngine engine,
    AuditCheckpointWriter checkpoints,
    IClock clock,
    IOptions<AuditLedgerOptions> options,
    ILogger<AuditSealerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sealer = options.Value.Sealer;
        if (!sealer.Enabled)
            return;

        AuditingLog.SealerStarted(logger, sealer.Interval, sealer.BatchSize);

        var lastEmission = DateTimeOffset.MinValue;
        var nextEmission = clock.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await engine.SealUntilDrainedAsync(stoppingToken).ConfigureAwait(false);

                if (checkpoints.CanSign && clock.UtcNow >= nextEmission)
                {
                    var startedAt = clock.UtcNow;
                    var emitted = await checkpoints.EmitChangedAsync(lastEmission, stoppingToken).ConfigureAwait(false);
                    if (emitted >= 0)
                    {
                        // Overlap by one sealing interval so a link sealed during this emission is picked up next time.
                        lastEmission = startedAt - sealer.Interval;
                        nextEmission = startedAt + sealer.CheckpointInterval;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                AuditingLog.SealRoundFailed(logger, ex);
            }

            try
            {
                await Task.Delay(sealer.Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
