using System.Collections.Concurrent;
using SharedKernel.Security.Totp;

namespace SharedKernel.Testing.Security;

/// <summary>An in-memory <see cref="ITotpStepUpStore"/> for tests.</summary>
public sealed class InMemoryTotpStepUpStore : ITotpStepUpStore
{
    private readonly ConcurrentDictionary<(string SubjectId, string SessionId), DateTimeOffset> _stepUps = new();

    /// <inheritdoc/>
    public ValueTask RecordAsync(
        string subjectId, string sessionId, DateTimeOffset verifiedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        ArgumentNullException.ThrowIfNull(sessionId);
        _stepUps[(subjectId, sessionId)] = verifiedAt;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<DateTimeOffset?> GetLastVerifiedAsync(string subjectId, string sessionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        ArgumentNullException.ThrowIfNull(sessionId);
        return ValueTask.FromResult(_stepUps.TryGetValue((subjectId, sessionId), out DateTimeOffset verifiedAt) ? verifiedAt : (DateTimeOffset?)null);
    }

    /// <summary>Removes every recorded step-up.</summary>
    public void Clear() => _stepUps.Clear();
}

/// <summary>An in-memory <see cref="IRecoveryCodeStore"/> for tests.</summary>
public sealed class InMemoryRecoveryCodeStore : IRecoveryCodeStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, StoredRecoveryCode>> _unused = new(StringComparer.Ordinal);

    /// <summary>Replaces a user's codes, as a service does when an enrollment is confirmed.</summary>
    /// <param name="subjectId">The user's subject id.</param>
    /// <param name="codes">The codes from <see cref="TotpEnrollment.StoredRecoveryCodes"/>.</param>
    public void Save(string subjectId, IEnumerable<StoredRecoveryCode> codes)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        ArgumentNullException.ThrowIfNull(codes);
        _unused[subjectId] = new ConcurrentDictionary<string, StoredRecoveryCode>(codes.ToDictionary(code => code.Id), StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<StoredRecoveryCode>> GetUnusedAsync(string subjectId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        IReadOnlyList<StoredRecoveryCode> codes = _unused.TryGetValue(subjectId, out var byId) ? [.. byId.Values] : [];
        return ValueTask.FromResult(codes);
    }

    /// <inheritdoc/>
    public ValueTask<bool> TryMarkUsedAsync(string subjectId, string codeId, DateTimeOffset usedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        ArgumentNullException.ThrowIfNull(codeId);
        return ValueTask.FromResult(_unused.TryGetValue(subjectId, out var byId) && byId.TryRemove(codeId, out _));
    }
}
