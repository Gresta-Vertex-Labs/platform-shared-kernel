using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

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

    /// <summary>
    /// Asserts a record with the given <paramref name="eventId"/> was captured with the message template
    /// <paramref name="template"/> (the <c>{OriginalFormat}</c> value, not the rendered message).
    /// </summary>
    /// <param name="records">The captured records.</param>
    /// <param name="eventId">The event id.</param>
    /// <param name="template">The exact template, placeholders included.</param>
    /// <returns>The first matching <see cref="LogRecord"/>.</returns>
    /// <exception cref="InvalidOperationException">No matching record was found.</exception>
    public static LogRecord ShouldHaveLoggedTemplate(this IReadOnlyList<LogRecord> records, EventId eventId, string template)
    {
        ArgumentNullException.ThrowIfNull(records);

        var sameEvent = records.Where(record => record.EventId == eventId).ToList();
        var match = sameEvent.FirstOrDefault(record => string.Equals(record.Template, template, StringComparison.Ordinal));
        return match ?? throw new InvalidOperationException(
            $"Expected a log record with EventId '{eventId}' and template '{template}', but "
            + (sameEvent.Count == 0
                ? "no record with that EventId was found."
                : $"found templates [{string.Join(", ", sameEvent.Select(record => $"'{record.Template ?? "(none)"}'"))}]."));
    }

    /// <summary>
    /// Asserts a record with the given <paramref name="eventId"/> carries an exception of
    /// <paramref name="exceptionType"/> (or a type derived from it).
    /// </summary>
    /// <param name="records">The captured records.</param>
    /// <param name="eventId">The event id.</param>
    /// <param name="exceptionType">The expected exception type.</param>
    /// <returns>The first matching <see cref="LogRecord"/>.</returns>
    /// <exception cref="InvalidOperationException">No matching record was found.</exception>
    public static LogRecord ShouldHaveLoggedWithException(this IReadOnlyList<LogRecord> records, EventId eventId, Type exceptionType)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(exceptionType);

        var sameEvent = records.Where(record => record.EventId == eventId).ToList();
        var match = sameEvent.FirstOrDefault(record => record.Exception is not null && exceptionType.IsInstanceOfType(record.Exception));
        return match ?? throw new InvalidOperationException(
            $"Expected a log record with EventId '{eventId}' and an exception of type '{exceptionType.FullName}', but "
            + (sameEvent.Count == 0
                ? "no record with that EventId was found."
                : $"found exceptions [{string.Join(", ", sameEvent.Select(record => $"'{record.Exception?.GetType().FullName ?? "(none)"}'"))}]."));
    }

    /// <summary>Asserts a record with the given <paramref name="eventId"/> exists and carries no exception.</summary>
    /// <param name="records">The captured records.</param>
    /// <param name="eventId">The event id.</param>
    /// <returns>The first matching <see cref="LogRecord"/>.</returns>
    /// <exception cref="InvalidOperationException">No such record exists, or every such record carries an exception.</exception>
    public static LogRecord ShouldHaveLoggedWithoutException(this IReadOnlyList<LogRecord> records, EventId eventId)
    {
        ArgumentNullException.ThrowIfNull(records);

        var sameEvent = records.Where(record => record.EventId == eventId).ToList();
        var match = sameEvent.FirstOrDefault(record => record.Exception is null);
        return match ?? throw new InvalidOperationException(
            sameEvent.Count == 0
                ? $"Expected a log record with EventId '{eventId}' and no exception, but no record with that EventId was found."
                : $"Expected a log record with EventId '{eventId}' and no exception, but all {sameEvent.Count} carried one.");
    }

    /// <summary>
    /// Asserts none of <paramref name="forbiddenValues"/> appears in a record's message, template, structured state
    /// values or exception message.
    /// </summary>
    /// <param name="records">The captured records.</param>
    /// <param name="forbiddenValues">Values (tenant ids, tokens, personal data) that must not appear.</param>
    /// <returns>The same records.</returns>
    /// <exception cref="InvalidOperationException">A forbidden value was found.</exception>
    public static IReadOnlyList<LogRecord> ShouldNotLeak(this IReadOnlyList<LogRecord> records, params string[] forbiddenValues)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(forbiddenValues);
        foreach (var forbidden in forbiddenValues)
        {
            ArgumentException.ThrowIfNullOrEmpty(forbidden, nameof(forbiddenValues));
        }

        var leaks = new List<string>();
        foreach (var record in records)
        {
            var label = $"EventId {record.EventId.Id}";

            void Scan(string location, string? value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }

                foreach (var forbidden in forbiddenValues)
                {
                    if (value.Contains(forbidden, StringComparison.Ordinal))
                    {
                        leaks.Add($"{label} {location} contains '{forbidden}'");
                    }
                }
            }

            Scan("message", record.Message);
            Scan("template", record.Template);
            Scan("exception message", record.Exception?.Message);
            if (record.State is not null)
            {
                foreach (var property in record.State)
                {
                    Scan($"property '{property.Key}'", property.Value?.ToString());
                }
            }
        }

        return leaks.Count == 0
            ? records
            : throw new InvalidOperationException(
                $"Expected no forbidden value in any log record, but found:{Environment.NewLine}{string.Join(Environment.NewLine, leaks.Distinct().Select(leak => " - " + leak))}");
    }

    /// <summary>
    /// Asserts every record follows the log conventions: L3 (one sentence ending in a period, PascalCase placeholders,
    /// no <c>{Error}</c>/<c>{Reason}</c>/<c>{ErrorMessage}</c>/<c>{Detail}</c>, no em-dash, no <c>key=value</c>
    /// parenthetical, identifier placeholders ending in <c>Name</c> or <c>Key</c> in single quotes), L4 (a record at
    /// <see cref="LogLevel.Error"/> or above, or one holding an exception as a value, carries the exception parameter)
    /// and L5 (no tenant, correlation, trace, actor, client, user, address, URL, host or idempotency-key placeholder).
    /// Every violation is listed in the exception message.
    /// </summary>
    /// <param name="records">The captured records.</param>
    /// <returns>The same records.</returns>
    /// <exception cref="InvalidOperationException">At least one convention is violated.</exception>
    public static IReadOnlyList<LogRecord> ShouldFollowLogConvention(this IReadOnlyList<LogRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var violations = LogConventions.Violations(records);
        return violations.Count == 0
            ? records
            : throw new InvalidOperationException(
                $"Log records violate {violations.Count} convention(s):{Environment.NewLine}{string.Join(Environment.NewLine, violations.Select(violation => " - " + violation))}");
    }

    /// <summary>
    /// Asserts every record's EventId lies in the package sub-block <paramref name="subBlockIndex"/> of the domain block
    /// starting at <paramref name="domainBase"/>.
    /// </summary>
    /// <param name="records">The captured records.</param>
    /// <param name="domainBase">A domain base constant of <c>LoggingEventIdRanges</c>.</param>
    /// <param name="subBlockIndex">The sub-block index, 0 to 9.</param>
    /// <returns>The same records.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="domainBase"/> or <paramref name="subBlockIndex"/> is invalid.</exception>
    /// <exception cref="InvalidOperationException">A record lies outside the sub-block.</exception>
    public static IReadOnlyList<LogRecord> ShouldOnlyLogEventIdsInSubBlock(
        this IReadOnlyList<LogRecord> records,
        int domainBase,
        int subBlockIndex)
    {
        ArgumentNullException.ThrowIfNull(records);

        var start = LoggingEventIdRanges.SubBlockStart(domainBase, subBlockIndex);
        var offenders = records
            .Where(record => !LoggingEventIdRanges.IsInSubBlock(record.EventId.Id, domainBase, subBlockIndex))
            .Select(record => record.EventId.Id)
            .Distinct()
            .Order()
            .ToList();
        return offenders.Count == 0
            ? records
            : throw new InvalidOperationException(
                $"Expected every EventId to lie in {start}-{start + 99} (sub-block {subBlockIndex} of {domainBase}), but found [{string.Join(", ", offenders)}].");
    }
}

/// <summary>Read-only assertions over the categories an <see cref="InMemoryLoggerFactory"/> created.</summary>
/// <remarks>Throws <see cref="InvalidOperationException"/> on failure and returns the factory so assertions chain.</remarks>
public static class LoggerFactoryAssertions
{
    /// <summary>
    /// Asserts a logger with the category of <paramref name="kernelType"/> was created: the category is the type's full
    /// name, as <c>ILogger&lt;T&gt;</c> produces it (generic arity and nesting markers removed).
    /// </summary>
    /// <param name="factory">The factory.</param>
    /// <param name="kernelType">The type the logger was requested for.</param>
    /// <returns>The factory.</returns>
    /// <exception cref="InvalidOperationException">No logger with that category exists.</exception>
    public static InMemoryLoggerFactory ShouldHaveCategory(this InMemoryLoggerFactory factory, Type kernelType)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(kernelType);

        var category = CategoryOf(kernelType);
        return factory.Loggers.ContainsKey(category)
            ? factory
            : throw new InvalidOperationException(
                $"Expected a logger with category '{category}', but the categories are [{string.Join(", ", factory.Loggers.Keys.Order(StringComparer.Ordinal).Select(key => $"'{key}'"))}].");
    }

    /// <summary>Asserts every created logger category starts with <paramref name="prefix"/> (ordinal).</summary>
    /// <param name="factory">The factory.</param>
    /// <param name="prefix">The required category prefix, for example a package namespace.</param>
    /// <returns>The factory.</returns>
    /// <exception cref="InvalidOperationException">A category does not start with the prefix.</exception>
    public static InMemoryLoggerFactory ShouldOnlyHaveCategoriesStartingWith(this InMemoryLoggerFactory factory, string prefix)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        var offenders = factory.Loggers.Keys
            .Where(key => !key.StartsWith(prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        return offenders.Count == 0
            ? factory
            : throw new InvalidOperationException(
                $"Expected every logger category to start with '{prefix}', but found [{string.Join(", ", offenders.Select(key => $"'{key}'"))}].");
    }

    private static string CategoryOf(Type type)
    {
        var name = type.FullName ?? type.Name;
        var arity = name.IndexOf('`', StringComparison.Ordinal);
        if (arity >= 0)
        {
            name = name[..arity];
        }

        return name.Replace('+', '.');
    }
}
