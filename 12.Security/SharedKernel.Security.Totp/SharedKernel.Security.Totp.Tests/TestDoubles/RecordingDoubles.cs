using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Security.Totp.Tests.TestDoubles;

internal sealed class RecordingAttemptThrottle : ITotpAttemptThrottle
{
    private readonly ConcurrentQueue<string> _checks = new();
    private readonly ConcurrentQueue<string> _attempts = new();

    public bool Throttled { get; set; }

    public IReadOnlyList<string> Checks => [.. _checks];

    public IReadOnlyList<string> Attempts => [.. _attempts];

    public ValueTask<bool> IsThrottledAsync(string identityKey, CancellationToken cancellationToken = default)
    {
        _checks.Enqueue(identityKey);
        return new(Throttled);
    }

    public ValueTask RecordAttemptAsync(string identityKey, CancellationToken cancellationToken = default)
    {
        _attempts.Enqueue(identityKey);
        return ValueTask.CompletedTask;
    }
}

internal sealed class CountingVerifier(ITotpVerifier inner) : ITotpVerifier
{
    private int _calls;

    public int Calls => _calls;

    public ValueTask<TotpVerificationResult> VerifyAsync(
        string identityKey,
        ReadOnlyMemory<byte> secret,
        string code,
        TotpParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        return inner.VerifyAsync(identityKey, secret, code, parameters, cancellationToken);
    }
}

internal sealed class CountingHasher : IOneWayHasher
{
    private readonly FakeOneWayHasher _inner = new();
    private readonly ConcurrentQueue<string> _verifiedHashes = new();

    public IReadOnlyList<string> VerifiedHashes => [.. _verifiedHashes];

    public string Hash(string secret) => _inner.Hash(secret);

    public HashVerificationResult Verify(string hash, string secret)
    {
        _verifiedHashes.Enqueue(hash);
        return _inner.Verify(hash, secret);
    }
}

internal sealed class RecordingStepUpStore(ITotpStepUpStore inner) : ITotpStepUpStore
{
    private readonly ConcurrentQueue<(string SubjectId, string SessionId, DateTimeOffset VerifiedAt, DateTimeOffset ExpiresAt)> _records = new();
    private int _reads;

    public IReadOnlyList<(string SubjectId, string SessionId, DateTimeOffset VerifiedAt, DateTimeOffset ExpiresAt)> Records => [.. _records];

    public int Reads => _reads;

    public ValueTask RecordAsync(
        string subjectId, string sessionId, DateTimeOffset verifiedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        _records.Enqueue((subjectId, sessionId, verifiedAt, expiresAt));
        return inner.RecordAsync(subjectId, sessionId, verifiedAt, expiresAt, cancellationToken);
    }

    public ValueTask<DateTimeOffset?> GetLastVerifiedAsync(string subjectId, string sessionId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reads);
        return inner.GetLastVerifiedAsync(subjectId, sessionId, cancellationToken);
    }
}

internal sealed class ControllableRecoveryCodeStore(IRecoveryCodeStore inner) : IRecoveryCodeStore
{
    private int _reads;

    public int Reads => _reads;

    public bool? TryMarkUsedResult { get; set; }

    public Func<ValueTask>? AfterRead { get; set; }

    public async ValueTask<IReadOnlyList<StoredRecoveryCode>> GetUnusedAsync(string subjectId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reads);
        IReadOnlyList<StoredRecoveryCode> codes = await inner.GetUnusedAsync(subjectId, cancellationToken);
        if (AfterRead is { } afterRead)
        {
            await afterRead();
        }

        return codes;
    }

    public ValueTask<bool> TryMarkUsedAsync(string subjectId, string codeId, DateTimeOffset usedAt, CancellationToken cancellationToken) =>
        TryMarkUsedResult is { } result ? new(result) : inner.TryMarkUsedAsync(subjectId, codeId, usedAt, cancellationToken);
}

// Maps the claims SecurityTestContextBuilder produces, like the OIDC mapper does with its defaults.
internal sealed class TestUserContextMapper(string authenticationType = "Bearer") : IUserContextMapper
{
    public string AuthenticationType { get; } = authenticationType;

    public IUserContext Map(ClaimsIdentity identity)
    {
        string? subject = identity.FindFirst(SecurityClaimTypes.Subject)?.Value;
        if (subject is null)
        {
            return AnonymousUserContext.Instance;
        }

        IdentityKind kind = identity.HasClaim("idtyp", "app") ? IdentityKind.ServicePrincipal : IdentityKind.User;
        return new UserContext(kind, subject, identity.Claims)
        {
            SessionId = identity.FindFirst(SecurityClaimTypes.SessionId)?.Value,
            AuthenticationMethods = [.. identity.FindAll(SecurityClaimTypes.AuthenticationMethod).Select(claim => claim.Value)],
        };
    }
}

internal sealed class TransformationLog
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyList<string> Entries => [.. _entries];

    public void Add(string entry) => _entries.Enqueue(entry);
}

// Adds a marker claim to the first identity, so a test can see that it ran and that its output was used.
internal sealed class MarkerClaimsTransformation(TransformationLog log) : IClaimsTransformation
{
    public const string ClaimType = "marker";

    public string Tag { get; init; } = "type";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        log.Add(Tag);
        return Task.FromResult(new ClaimsPrincipal(principal.Identities.Select((identity, index) =>
            index == 0 ? new ClaimsIdentity(identity, [new Claim(ClaimType, Tag)]) : identity)));
    }
}
