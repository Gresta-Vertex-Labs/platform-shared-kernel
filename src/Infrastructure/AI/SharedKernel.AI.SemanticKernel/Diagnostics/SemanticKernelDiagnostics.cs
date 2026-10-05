using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.AI.Abstractions.Constants;

namespace SharedKernel.AI.SemanticKernel.Diagnostics;

/// <summary>
/// This package's own <see cref="ActivitySource"/> and <see cref="Meter"/> instances, named from
/// <see cref="IntelligenceWellKnown.ActivitySourceName"/>/<see cref="IntelligenceWellKnown.MeterName"/>
/// so <c>13.ServiceDefaults</c> can wire both by string name with no <c>ProjectReference</c> to
/// <c>10.Intelligence</c>.
/// </summary>
internal static class SemanticKernelDiagnostics
{
    /// <summary>The shared <see cref="ActivitySource"/> for this provider's spans.</summary>
    public static readonly ActivitySource ActivitySource = new(IntelligenceWellKnown.ActivitySourceName);

    /// <summary>The shared <see cref="Meter"/> for this provider's instruments.</summary>
    public static readonly Meter Meter = new(IntelligenceWellKnown.MeterName);
}
