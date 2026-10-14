using System.Diagnostics;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Persistence.EfCore.Auditing.Diagnostics;

/// <summary>
/// The spans of the audit ledger, started on the one persistence <see cref="ActivitySource"/> and named
/// <c>persistence audit.{operation}</c>. They are <see cref="ActivityKind.Internal"/>: the database calls inside them
/// are the <c>Client</c> spans Npgsql emits. A failure is recorded by the shared Primitives recorder; a cancellation is
/// never an error.
/// </summary>
internal static class AuditTracing
{
    /// <summary>The span of a checkpoint creation.</summary>
    internal const string CheckpointSpan = "persistence audit.checkpoint";

    /// <summary>The span of one sealing pass.</summary>
    internal const string SealSpan = "persistence audit.seal";

    /// <summary>The span of one chain verification.</summary>
    internal const string VerifySpan = "persistence audit.verify";

    internal static Activity? Start(string spanName) =>
        PersistenceActivitySource.Source.StartActivity(spanName, ActivityKind.Internal);

    internal static void Succeed(Activity? activity) =>
        activity?.SetTag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success);

    internal static void Fail(Activity? activity, Exception exception) =>
        activity?.SetTag(WellKnownTagKeys.Outcome, activity.RecordException(exception));
}
