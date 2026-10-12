using System.Diagnostics;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Idempotency.Redis.Diagnostics;

/// <summary>The span source and the tag vocabulary of the Redis idempotency store.</summary>
/// <remarks>
/// The source and meter are named <see cref="IdempotencyTelemetry.SourceName"/>; the EF Core provider uses the same
/// names and the <see cref="IdempotencyTagKeys.Provider"/> tag tells the two apart.
/// </remarks>
internal static class RedisIdempotencyTelemetry
{
    /// <summary>The <see cref="IdempotencyTagKeys.Provider"/> value of this package.</summary>
    internal const string ProviderName = "redis";

    /// <summary>The span source of this package.</summary>
    internal static readonly ActivitySource Source = new(
        IdempotencyTelemetry.SourceName,
        TelemetryVersion.Of(typeof(RedisIdempotencyTelemetry).Assembly));

    /// <summary>
    /// Records a classified outage on <paramref name="activity"/> without its provider exception, whose text can name
    /// the key.
    /// </summary>
    /// <param name="activity">The span; may be <see langword="null"/>.</param>
    /// <param name="unavailable">The outage about to be thrown or let through.</param>
    /// <returns>The outcome for the duration histogram.</returns>
    internal static string RecordOutage(Activity? activity, IdempotencyStoreUnavailableException unavailable) =>
        activity.RecordException(new IdempotencyStoreUnavailableException(unavailable.Error));

    /// <summary>The <see cref="IdempotencyTagKeys.Purpose"/> value of <paramref name="purpose"/>.</summary>
    /// <param name="purpose">A defined purpose.</param>
    /// <returns><c>request</c> or <c>message</c>.</returns>
    internal static string PurposeTag(IdempotencyPurpose purpose) => purpose switch
    {
        IdempotencyPurpose.Request => IdempotencyTelemetry.PurposeRequest,
        IdempotencyPurpose.Message => IdempotencyTelemetry.PurposeMessage,
        _ => throw new UnreachableException(),
    };
}
