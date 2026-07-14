using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Testing.Logging;

/// <summary>
/// An in-memory <see cref="ILoggerFactory"/> test double that lazily creates and caches one
/// <see cref="InMemoryLogger"/> per category name.
/// </summary>
/// <remarks>
/// This fake IS the entire logging pipeline for the test under composition — it does not compose
/// with additional providers registered via <see cref="AddProvider"/>.
/// </remarks>
public sealed class InMemoryLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentDictionary<string, InMemoryLogger> _loggers = new();

    /// <summary>A snapshot of every logger category created so far.</summary>
    public IReadOnlyDictionary<string, InMemoryLogger> Loggers => _loggers;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => GetLogger(categoryName);

    /// <summary>
    /// Returns the <see cref="InMemoryLogger"/> for <paramref name="categoryName"/>, creating it
    /// if it does not already exist. Exposed under a more discoverable name than
    /// <see cref="CreateLogger"/> for assertion call sites.
    /// </summary>
    public InMemoryLogger GetLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, static _ => new InMemoryLogger());

    /// <summary>Documented no-op — this fake does not compose with additional logging providers.</summary>
    public void AddProvider(ILoggerProvider provider)
    {
        // No-op by design: see remarks on the type.
    }

    /// <summary>Documented no-op — there is nothing to release.</summary>
    public void Dispose()
    {
        // No-op by design: nothing to release.
    }
}
