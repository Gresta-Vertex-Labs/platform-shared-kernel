using Microsoft.Extensions.Logging;

namespace SharedKernel.Testing.Logging;

/// <summary>
/// Read-only assertion helpers over a captured <see cref="LogRecord"/> list (typically
/// <see cref="InMemoryLogger.Records"/>).
/// </summary>
/// <remarks>
/// All members throw a plain <see cref="InvalidOperationException"/> on failure — zero
/// test-framework dependency, mirroring <c>InMemoryMessageBus</c>'s <c>Should*</c> naming and
/// <c>PagedListAssertions</c>'s exception convention. These are read-only queries; they never
/// mutate the supplied record list.
/// </remarks>
public static class LoggerAssertions
{
    /// <summary>Asserts a record with the given <paramref name="eventId"/> was captured.</summary>
    /// <returns>The first matching <see cref="LogRecord"/>.</returns>
    /// <exception cref="InvalidOperationException">No matching record was found.</exception>
    public static LogRecord ShouldHaveLogged(this IReadOnlyList<LogRecord> records, EventId eventId)
    {
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in records)
        {
            if (record.EventId == eventId)
            {
                return record;
            }
        }

        throw new InvalidOperationException($"Expected a log record with EventId '{eventId}', but none was found.");
    }

    /// <summary>
    /// Asserts a record with the given <paramref name="eventId"/> and <paramref name="level"/>
    /// was captured.
    /// </summary>
    /// <returns>The first matching <see cref="LogRecord"/>.</returns>
    /// <exception cref="InvalidOperationException">No matching record was found.</exception>
    public static LogRecord ShouldHaveLogged(this IReadOnlyList<LogRecord> records, EventId eventId, LogLevel level)
    {
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in records)
        {
            if (record.EventId == eventId && record.LogLevel == level)
            {
                return record;
            }
        }

        throw new InvalidOperationException(
            $"Expected a log record with EventId '{eventId}' and LogLevel '{level}', but none was found.");
    }

    /// <summary>
    /// Asserts a record with the given <paramref name="eventId"/> was captured with a structured
    /// property named <paramref name="propertyName"/> whose value equals
    /// <paramref name="expectedValue"/>. Asserts by structured property value — never by
    /// rendered-message string matching.
    /// </summary>
    /// <returns>The first matching <see cref="LogRecord"/>.</returns>
    /// <exception cref="InvalidOperationException">No matching record was found.</exception>
    public static LogRecord ShouldHaveLoggedWithProperty(
        this IReadOnlyList<LogRecord> records,
        EventId eventId,
        string propertyName,
        object? expectedValue)
    {
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in records)
        {
            if (record.EventId == eventId
                && record.TryGetProperty(propertyName, out var actualValue)
                && Equals(actualValue, expectedValue))
            {
                return record;
            }
        }

        throw new InvalidOperationException(
            $"Expected a log record with EventId '{eventId}' and property '{propertyName}' equal to "
            + $"'{expectedValue}', but none was found.");
    }

    /// <summary>Asserts no record with the given <paramref name="eventId"/> was captured.</summary>
    /// <exception cref="InvalidOperationException">A matching record was found.</exception>
    public static void ShouldNotHaveLogged(this IReadOnlyList<LogRecord> records, EventId eventId)
    {
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in records)
        {
            if (record.EventId == eventId)
            {
                throw new InvalidOperationException(
                    $"Expected no log record with EventId '{eventId}', but at least one was found.");
            }
        }
    }

    /// <summary>
    /// Asserts exactly <paramref name="expectedCount"/> records with the given
    /// <paramref name="eventId"/> were captured.
    /// </summary>
    /// <exception cref="InvalidOperationException">The actual count does not match.</exception>
    public static void ShouldHaveLoggedCount(this IReadOnlyList<LogRecord> records, EventId eventId, int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(records);

        var actualCount = records.Count(record => record.EventId == eventId);
        if (actualCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} log record(s) with EventId '{eventId}', but found {actualCount}.");
        }
    }
}
