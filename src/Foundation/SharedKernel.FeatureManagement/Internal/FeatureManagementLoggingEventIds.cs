using SharedKernel.Primitives.Logging;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// This package's <c>EventId</c>s: the sub-block <c>1300</c>-<c>1399</c> of <c>01.Core</c>'s range
/// (<see cref="LoggingEventIdRanges.Core"/>, sub-block 3), as recorded in the domain's ownership table.
/// </summary>
internal static class FeatureManagementLoggingEventIds
{
    public const int Base = LoggingEventIdRanges.Core + (3 * LoggingEventIdRanges.PackageSubBlockWidth);

    public const int EvaluationFailed = Base + 1;
}
