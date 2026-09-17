using System.Collections.Concurrent;
using SharedKernel.Security.Oidc.Revocation;

namespace SharedKernel.Security.Oidc.Tests.Infrastructure;

internal sealed class RecordingRevocationCheck : ITokenRevocationCheck
{
    private readonly ConcurrentQueue<TokenRevocationRequest> _requests = new();

    public Func<TokenRevocationRequest, bool> IsRevoked { get; set; } = _ => false;

    public Exception? Failure { get; set; }

    public IReadOnlyList<TokenRevocationRequest> Requests => [.. _requests];

    public ValueTask<bool> IsRevokedAsync(TokenRevocationRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        if (Failure is not null)
        {
            throw Failure;
        }

        return ValueTask.FromResult(IsRevoked(request));
    }
}

internal sealed class RecordingRevocationCache : ITokenRevocationCache
{
    private readonly ConcurrentDictionary<string, bool> _answers = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string TokenHash, bool IsRevoked, DateTimeOffset ExpiresAt)> _writes = new();

    public Exception? ReadFailure { get; set; }

    public Exception? WriteFailure { get; set; }

    public IReadOnlyList<(string TokenHash, bool IsRevoked, DateTimeOffset ExpiresAt)> Writes => [.. _writes];

    public void Seed(string tokenHash, bool isRevoked) => _answers[tokenHash] = isRevoked;

    public ValueTask<bool?> GetAsync(string tokenHash, CancellationToken cancellationToken)
    {
        if (ReadFailure is not null)
        {
            throw ReadFailure;
        }

        return ValueTask.FromResult<bool?>(_answers.TryGetValue(tokenHash, out bool answer) ? answer : null);
    }

    public ValueTask SetAsync(string tokenHash, bool isRevoked, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        if (WriteFailure is not null)
        {
            throw WriteFailure;
        }

        _writes.Enqueue((tokenHash, isRevoked, expiresAt));
        _answers[tokenHash] = isRevoked;
        return ValueTask.CompletedTask;
    }
}
