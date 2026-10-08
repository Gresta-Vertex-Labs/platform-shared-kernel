using SharedKernel.Caching.Redis.HashStore;
using SharedKernel.Cryptography.Totp;

namespace Shop.Ordering.Infrastructure;

/// <summary>
/// Accepts each authenticator time step once per identity, across replicas: the first replica to increment the step's
/// counter in Redis wins, every replay sees a count above one.
/// </summary>
public sealed class RedisTotpReplayGuard(IRedisHashService hashes) : ITotpReplayGuard
{
    public async ValueTask<bool> TryAcceptTimeStepAsync(
        string identityKey,
        long timeStep,
        TimeSpan retention,
        CancellationToken cancellationToken = default
    ) =>
        await hashes.IncrementFieldAsync(
            $"ordering:totp:replay:{identityKey}",
            timeStep.ToString(System.Globalization.CultureInfo.InvariantCulture),
            timeToLive: retention,
            ct: cancellationToken
        ) == 1;
}

/// <summary>At most five wrong codes per identity in five minutes, counted in Redis so every replica agrees.</summary>
public sealed class RedisTotpAttemptThrottle(IRedisHashService hashes) : ITotpAttemptThrottle
{
    private const int MaxAttempts = 5;
    private const string Field = "attempts";
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public async ValueTask<bool> IsThrottledAsync(
        string identityKey,
        CancellationToken cancellationToken = default
    )
    {
        var lookup = await hashes.GetFieldAsync(
            Key(identityKey),
            Field,
            TotpJsonContext.Default.Int64,
            cancellationToken
        );
        return lookup.TryGetValue(out long attempts) && attempts >= MaxAttempts;
    }

    public async ValueTask RecordAttemptAsync(
        string identityKey,
        CancellationToken cancellationToken = default
    ) =>
        await hashes.IncrementFieldAsync(
            Key(identityKey),
            Field,
            timeToLive: Window,
            ct: cancellationToken
        );

    private static string Key(string identityKey) => $"ordering:totp:attempts:{identityKey}";
}

[System.Text.Json.Serialization.JsonSerializable(typeof(long))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DateTimeOffset))]
public sealed partial class TotpJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
