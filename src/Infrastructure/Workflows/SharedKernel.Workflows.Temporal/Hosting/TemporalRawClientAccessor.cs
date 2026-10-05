using Microsoft.Extensions.Logging;
using SharedKernel.Workflows.Temporal.Logging;
using Temporalio.Client;

namespace SharedKernel.Workflows.Temporal.Hosting;

/// <summary>The default <see cref="ITemporalRawClientAccessor"/> — a thin wrapper over the registered client.</summary>
internal sealed class TemporalRawClientAccessor : ITemporalRawClientAccessor
{
    public TemporalRawClientAccessor(ITemporalClient client, ILogger<TemporalRawClientAccessor> logger)
    {
        Client = client;
        WorkflowLog.RawClientAccessEnabled(logger);
    }

    /// <inheritdoc />
    public ITemporalClient Client { get; }
}
