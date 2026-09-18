using SharedKernel.Primitives.Logging;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// This package's <c>EventId</c>s: the fourth 100-wide sub-block of <c>01.Core</c>'s range (1300-1399),
/// following the package order Primitives, Core, Configuration, FeatureManagement.
/// </summary>
internal static class FeatureManagementEventIds
{
    public const int Base = LoggingEventIdRanges.Core + 300;

    public const int EvaluationFailed = Base + 1;
}
