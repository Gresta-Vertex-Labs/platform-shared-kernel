namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// Tests that change process-wide state (environment variables read by the OpenTelemetry resource detectors) or
/// count instruments by name run one at a time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NonParallelTelemetryCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "NonParallelTelemetry";
}
