using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Options controlling the logging behavior of the request pipeline.
/// </summary>
/// <remarks>
/// Registered with its default values by <c>AddSharedKernelApplication</c> (logging is always on), via
/// <c>.ValidateDataAnnotations().ValidateOnStart()</c> — an invalid <see cref="SlowRequestThreshold"/> fails when the
/// host starts, not on the first request. Bind or configure via the
/// standard <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> pattern to override
/// <see cref="SlowRequestThreshold"/>.
/// </remarks>
public sealed class ApplicationLoggingOptions
{
    /// <summary>
    /// Gets or sets the elapsed-time threshold above which a successful request is logged at
    /// <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/> instead of
    /// <see cref="Microsoft.Extensions.Logging.LogLevel.Information"/>.
    /// </summary>
    /// <value>Defaults to 500 milliseconds. Must be greater than <see cref="TimeSpan.Zero"/>.</value>
    [Range(
        typeof(TimeSpan),
        "00:00:00.0000001",
        "10675199.02:48:05.4775807",
        ErrorMessage = "SlowRequestThreshold must be greater than zero.")]
    public TimeSpan SlowRequestThreshold { get; set; } = TimeSpan.FromMilliseconds(500);
}
