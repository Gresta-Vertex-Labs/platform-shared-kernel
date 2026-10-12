using Microsoft.Extensions.Logging;

namespace SharedKernel.FeatureManagement.Diagnostics;

/// <summary>The package's log messages, source generated. The category is the calling provider's <c>ILogger&lt;T&gt;</c>.</summary>
internal static partial class FeatureManagementLog
{
    [LoggerMessage(
        EventId = Internal.FeatureManagementLoggingEventIds.EvaluationFailed,
        Level = LogLevel.Error,
        Message = "Evaluating the feature flag '{FlagKey}' failed, so the caller's default value was used.")]
    internal static partial void EvaluationFailed(ILogger logger, string flagKey, Exception exception);
}
