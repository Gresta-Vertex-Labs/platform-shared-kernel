using SharedKernel.Application.Auditing;

namespace SharedKernel.Application.Behaviors.Tests.Support;

/// <summary>A minimal <see cref="IAuditTrailWriter"/> double recording every recorded entry.</summary>
internal sealed class FakeAuditTrailWriter(List<string>? sequence = null) : IAuditTrailWriter
{
    public List<AuditEntry> RecordedEntries { get; } = [];

    public Exception? FailWith { get; set; }

    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
            throw FailWith;

        RecordedEntries.Add(entry);
        sequence?.Add("audit.record");
        return Task.CompletedTask;
    }
}
