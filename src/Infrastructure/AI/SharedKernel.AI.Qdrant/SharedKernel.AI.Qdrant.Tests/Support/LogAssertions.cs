using FluentAssertions;
using SharedKernel.Testing.Logging;

namespace SharedKernel.AI.Qdrant.Tests.Support;

/// <summary>Log assertions for the "detail reaches the log through the exception, never through the message" rule.</summary>
internal static class LogAssertions
{
    /// <summary>
    /// Asserts no forbidden value appears in any record's formatted message, template or structured state. The exception
    /// parameter is deliberately not scanned: provider detail is allowed (and expected) to travel there.
    /// </summary>
    public static void ShouldNotLeakIntoMessageOrState(this IReadOnlyList<LogRecord> records, params string[] forbidden)
    {
        foreach (var record in records)
        {
            var scanned = new List<string?> { record.Message, record.Template };
            if (record.State is not null)
            {
                scanned.AddRange(record.State.Select(p => p.Value?.ToString()));
            }

            foreach (var text in scanned.Where(t => !string.IsNullOrEmpty(t)))
            {
                text.Should().NotContainAny(forbidden, $"EventId {record.EventId.Id} must not carry provider or content text outside its exception");
            }
        }
    }
}
