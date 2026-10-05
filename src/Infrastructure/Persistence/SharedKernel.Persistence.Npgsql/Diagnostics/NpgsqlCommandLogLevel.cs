using Microsoft.Extensions.Logging;

namespace SharedKernel.Persistence.Npgsql.Diagnostics;

/// <summary>
/// Wraps the logger factory handed to Npgsql so that its per-command log (category <see cref="CommandCategory"/>,
/// "Command execution completed ...: {CommandText}", logged by Npgsql at <see cref="LogLevel.Information"/>) is written
/// at <see cref="LogLevel.Debug"/>.
/// </summary>
/// <remarks>
/// At the usual <c>Information</c> minimum level Npgsql would otherwise log every SQL statement a service runs — noise
/// at best, and a statement's text can carry literals. To see the statements, enable <c>Debug</c> for
/// <c>Npgsql.Command</c>. Every other Npgsql category and level is passed through unchanged.
/// </remarks>
internal sealed class NpgsqlCommandLogLevel(ILoggerFactory inner) : ILoggerFactory
{
    /// <summary>Npgsql's command logging category.</summary>
    internal const string CommandCategory = "Npgsql.Command";

    public ILogger CreateLogger(string categoryName)
    {
        var logger = inner.CreateLogger(categoryName);
        return string.Equals(categoryName, CommandCategory, StringComparison.Ordinal) ? new DowngradingLogger(logger) : logger;
    }

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    // The wrapped factory belongs to the container; this wrapper owns nothing.
    public void Dispose()
    {
    }

    private sealed class DowngradingLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(Map(logLevel));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(Map(logLevel), eventId, state, exception, formatter);

        private static LogLevel Map(LogLevel level) => level == LogLevel.Information ? LogLevel.Debug : level;
    }
}
