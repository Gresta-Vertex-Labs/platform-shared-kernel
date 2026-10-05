using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Scheduling.Options;

/// <summary>
/// Process-wide configuration for the scheduling hosted loop.
/// </summary>
/// <remarks>
/// Bound from configuration under <see cref="SectionName"/> and validated eagerly at
/// <c>IHost.StartAsync()</c> via <c>ValidateDataAnnotations().ValidateOnStart()</c> — a misconfigured
/// value fails startup, never silently at first use (SK0022-compliant: <see cref="SectionName"/> is the
/// only place this section path is spelled out; every call site references the constant).
/// </remarks>
public sealed class SchedulingOptions
{
    /// <summary>The configuration section path this options type binds from.</summary>
    public const string SectionName = "SharedKernel:Scheduling";

    /// <summary>
    /// Gets or sets how often the hosted loop wakes up to check every registered job's due time.
    /// </summary>
    /// <remarks>
    /// This is also the misfire-detection threshold: a job observed more than one
    /// <see cref="TickInterval"/> past its due time is treated as a misfire rather than an ordinary
    /// on-time fire. Defaults to one second.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:10:00")]
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the default per-occurrence distributed-lock claim TTL used for a job that does not
    /// set its own <c>ScheduledJobOptions.LockExpiry</c>. Only meaningful when an
    /// <c>IDistributedLockService</c> is registered. Defaults to five minutes — generous for typical
    /// hourly/daily jobs; a job firing every few seconds should override this per-job.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan DefaultLockExpiry { get; set; } = TimeSpan.FromMinutes(5);
}
