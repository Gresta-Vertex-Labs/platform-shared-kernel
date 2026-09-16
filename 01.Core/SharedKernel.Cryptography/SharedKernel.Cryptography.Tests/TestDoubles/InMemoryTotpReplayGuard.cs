using System.Collections.Concurrent;
using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Cryptography.Tests.TestDoubles;

internal sealed class InMemoryTotpReplayGuard : ITotpReplayGuard
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, long> _lastAccepted = new(StringComparer.Ordinal);

    public ConcurrentQueue<TimeSpan> Retentions { get; } = new();

    public ValueTask<bool> TryAcceptTimeStepAsync(
        string identityKey,
        long timeStep,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        Retentions.Enqueue(retention);
        lock (_gate)
        {
            if (_lastAccepted.TryGetValue(identityKey, out long last) && timeStep <= last)
            {
                return new ValueTask<bool>(false);
            }

            _lastAccepted[identityKey] = timeStep;
            return new ValueTask<bool>(true);
        }
    }
}
