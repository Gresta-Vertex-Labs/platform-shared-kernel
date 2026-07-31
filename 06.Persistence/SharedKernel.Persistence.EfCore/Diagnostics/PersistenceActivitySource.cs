using System.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// The shared <see cref="ActivitySource"/> for distributed-tracing spans emitted by
/// <c>EfRepository{TAggregate,TId}</c>/<c>EfReadRepository{TAggregate,TId}</c> repository operations.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>02.Caching</c>'s <c>SharedKernel.Caching</c>/<c>"1.0"</c> <see cref="ActivitySource"/>
/// precedent exactly (<c>SharedKernel.Caching.FusionCache/Implementations/FusionCacheService.cs</c>).
/// BCL <see cref="System.Diagnostics.ActivitySource"/> — zero new NuGet dependency, AOT-safe.
/// </para>
/// <para>
/// <strong>THIS EXACT NAME/VERSION IS THE COORDINATION POINT for <c>13.ServiceDefaults</c>' paired
/// phase (P-326, <c>WithPersistenceTelemetry</c>-shaped wiring)</strong> — do not rename without
/// updating that consumer (WO-051/P-319).
/// </para>
/// </remarks>
internal static class PersistenceActivitySource
{
    /// <summary>The shared <see cref="ActivitySource"/> instance for this domain.</summary>
    public static readonly ActivitySource Source = new("SharedKernel.Persistence", "1.0");
}
