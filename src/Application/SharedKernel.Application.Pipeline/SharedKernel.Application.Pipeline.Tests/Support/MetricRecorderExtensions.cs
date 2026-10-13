using SharedKernel.Application.Pipeline.Metrics;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Application.Pipeline.Tests.Support;

internal static class MetricRecorderExtensions
{
    /// <summary>
    /// Asserts no request-duration measurement of <paramref name="requestType"/> carries <c>error.type</c>. The meter is
    /// shared by every test in the assembly, so the check is scoped to one request type.
    /// </summary>
    public static MetricRecorder ShouldNotCarryErrorTypeFor(this MetricRecorder recorder, Type requestType)
    {
        var offenders = recorder.MeasurementsOf(ApplicationMetrics.RequestDurationName)
            .Where(measurement => Equals(measurement.TryGetTag("application.request_type", out var value) ? value : null, requestType.FullName))
            .Where(measurement => measurement.TryGetTag("error.type", out _))
            .ToList();

        return offenders.Count == 0
            ? recorder
            : throw new InvalidOperationException(
                $"Expected no duration measurement of '{requestType.FullName}' to carry 'error.type', but {offenders.Count} did.");
    }
}
