using System.Diagnostics;

namespace SharedKernel.Persistence.Dapper.Diagnostics;

/// <summary>
/// The shared <see cref="ActivitySource"/> for spans emitted by
/// <see cref="ReadModels.DapperReadService"/>/<see cref="ReadModels.DapperCommandService"/> query and
/// command methods.
/// </summary>
/// <remarks>
/// Mirrors <c>SharedKernel.Persistence.EfCore.Diagnostics.PersistenceActivitySource</c>'s own
/// name/version shape and its "wire by string name only" consumption pattern in
/// <c>13.ServiceDefaults</c>.
/// </remarks>
internal static class DapperActivitySource
{
    /// <summary>The public name a host wires into its <c>TracerProvider</c> by string.</summary>
    public const string Name = "SharedKernel.Persistence.Dapper";

    /// <summary>The shared <see cref="ActivitySource"/> instance for this package.</summary>
    public static readonly ActivitySource Source = new(Name, "1.0");
}
