using Microsoft.Extensions.Logging;

namespace SharedKernel.Testing.Logging;

/// <summary>
/// A single captured log entry recorded by <see cref="InMemoryLogger"/>.
/// </summary>
/// <remarks>
/// <see cref="Message"/> is always the fully formatted message produced by the caller-supplied
/// formatter delegate — it is never re-derived from <see cref="State"/>. <see cref="State"/> is
/// populated only when the logged <c>TState</c> implements
/// <see cref="IReadOnlyList{T}"/> of <see cref="KeyValuePair{TKey,TValue}"/> of
/// <see cref="string"/> and <see cref="object"/> — the exact shape both
/// <c>[LoggerMessage]</c>'s source-generated state struct and standard structured-logging calls
/// produce.
/// </remarks>
public sealed record LogRecord
{
    /// <summary>The event id supplied to the logging call.</summary>
    public required EventId EventId { get; init; }

    /// <summary>The log level supplied to the logging call.</summary>
    public required LogLevel LogLevel { get; init; }

    /// <summary>The fully formatted message, produced by the caller-supplied formatter delegate.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// The structured state properties, when <c>TState</c> implements
    /// <see cref="IReadOnlyList{T}"/> of <see cref="KeyValuePair{TKey,TValue}"/> of
    /// <see cref="string"/> and <see cref="object"/>; otherwise <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>>? State { get; init; }

    /// <summary>The exception supplied to the logging call, if any.</summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// The active <see cref="InMemoryLogger.BeginScope{TState}(TState)"/> stack at the moment this
    /// record was logged, ordered outer-to-inner.
    /// </summary>
    public required IReadOnlyList<object?> Scopes { get; init; }

    /// <summary>
    /// Attempts to read a structured property from <see cref="State"/> by exact, case-sensitive
    /// key match — never parses <see cref="Message"/>.
    /// </summary>
    /// <param name="name">The property name to look up (case-sensitive).</param>
    /// <param name="value">The matched value, when found.</param>
    /// <returns><see langword="true"/> when a matching key was found; otherwise <see langword="false"/>.</returns>
    public bool TryGetProperty(string name, out object? value)
    {
        if (State is not null)
        {
            foreach (var kvp in State)
            {
                if (string.Equals(kvp.Key, name, StringComparison.Ordinal))
                {
                    value = kvp.Value;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }
}
