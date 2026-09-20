using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;

namespace SharedKernel.Persistence.Dapper.Options;

/// <summary>
/// Configuration for <see cref="ReadModels.DapperReadService"/>/<see cref="ReadModels.DapperCommandService"/>'s
/// default per-call command timeout.
/// </summary>
/// <remarks>
/// Bound from <see cref="SectionName"/> via <c>AddSharedKernelDapper(IServiceCollection,
/// IConfiguration,...)</c>. Every query/command method also accepts an explicit
/// <c>commandTimeout</c> parameter that overrides this default for one call.
/// </remarks>
public sealed class DapperPersistenceOptions : ISectionBoundOptions
{
    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Persistence:Dapper";

    /// <summary>
    /// The default command timeout (seconds) applied to a query/command call that does not supply
    /// its own <c>commandTimeout</c> argument. <see langword="null"/> defers to Dapper's/the
    /// underlying provider's own default.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? DefaultCommandTimeoutSeconds { get; set; }
}
