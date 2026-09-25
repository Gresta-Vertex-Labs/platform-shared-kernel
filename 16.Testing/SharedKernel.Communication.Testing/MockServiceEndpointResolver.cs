using System.Collections.Concurrent;
using SharedKernel.Communication.Internal.Resolvers;

namespace SharedKernel.Testing.Communication;

/// <summary>
/// In-memory test double for <see cref="IServiceEndpointResolver"/>.
/// </summary>
/// <remarks>
/// Mirrors the production resolver's "never throws" contract — an unconfigured service name
/// falls back to a deterministic, non-throwing default URI rather than throwing.
/// </remarks>
public sealed class MockServiceEndpointResolver : IServiceEndpointResolver
{
    private readonly ConcurrentDictionary<string, Uri> _configured = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _resolvedNames = new();

    /// <summary>Registers a fixed resolution result for <paramref name="serviceName"/>.</summary>
    /// <param name="serviceName">The logical service name.</param>
    /// <param name="uri">The URI to return for <paramref name="serviceName"/>.</param>
    public void Configure(string serviceName, Uri uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(uri);

        _configured[serviceName] = uri;
    }

    /// <inheritdoc />
    public ValueTask<Uri> ResolveAsync(string serviceName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        _resolvedNames.Enqueue(serviceName);

        if (_configured.TryGetValue(serviceName, out var uri))
            return ValueTask.FromResult(uri);

        // Deterministic, non-throwing fallback mirroring the production resolver's contract.
        return ValueTask.FromResult(new Uri($"http://{serviceName}.default.svc.cluster.local"));
    }

    /// <summary>Returns every service name ever passed to <see cref="ResolveAsync"/>, in call order.</summary>
    /// <returns>The recorded service names.</returns>
    public IReadOnlyList<string> GetResolvedNames() => _resolvedNames.ToArray();
}
