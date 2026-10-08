using SharedKernel.Caching.Redis.HashStore;
using SharedKernel.Security.Totp;
using Shop.Ordering.Infrastructure;

namespace Shop.Ordering.Api;

/// <summary>When each session last passed an authenticator step-up, in Redis so every replica sees it.</summary>
public sealed class RedisTotpStepUpStore(IRedisHashService hashes) : ITotpStepUpStore
{
    public async ValueTask RecordAsync(
        string subjectId,
        string sessionId,
        DateTimeOffset verifiedAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken
    ) =>
        await hashes.SetFieldAsync(
            Key(subjectId),
            sessionId,
            verifiedAt,
            TotpJsonContext.Default.DateTimeOffset,
            expiresAt - verifiedAt,
            cancellationToken
        );

    public async ValueTask<DateTimeOffset?> GetLastVerifiedAsync(
        string subjectId,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        var lookup = await hashes.GetFieldAsync(
            Key(subjectId),
            sessionId,
            TotpJsonContext.Default.DateTimeOffset,
            cancellationToken
        );
        return lookup.TryGetValue(out DateTimeOffset verifiedAt) ? verifiedAt : null;
    }

    private static string Key(string subjectId) => $"ordering:totp:stepup:{subjectId}";
}

/// <summary>The Shop does not issue recovery codes: a user who loses the app re-enrolls.</summary>
public sealed class NoRecoveryCodeStore : IRecoveryCodeStore
{
    public ValueTask<IReadOnlyList<StoredRecoveryCode>> GetUnusedAsync(
        string subjectId,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<IReadOnlyList<StoredRecoveryCode>>([]);

    public ValueTask<bool> TryMarkUsedAsync(
        string subjectId,
        string codeId,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(false);
}
