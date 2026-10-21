using Microsoft.Extensions.Options;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>Rejects an <see cref="OtlpExporterMode"/> value the enum does not define (a mistyped number in configuration).</summary>
internal sealed class SharedKernelTelemetryOptionsValidator : IValidateOptions<SharedKernelTelemetryOptions>
{
    public ValidateOptionsResult Validate(string? name, SharedKernelTelemetryOptions options)
    {
        if (Enum.IsDefined(options.OtlpExporter))
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"The setting '{SharedKernelTelemetryOptions.SectionName}:{nameof(SharedKernelTelemetryOptions.OtlpExporter)}' " +
            "must be 'Always' or 'WhenEndpointConfigured'.");
    }
}
