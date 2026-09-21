using System.Collections.Concurrent;
using SharedKernel.Application.Auditing;

namespace SharedKernel.Persistence.Testing;

/// <summary>
/// In-memory fake implementation of the shared <see cref="IAuditTrailWriter"/>
/// (<c>SharedKernel.Application.Abstractions</c>) for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Records the caller-supplied <see cref="AuditEntry"/> verbatim, with none of the identity resolution the real
/// ledger writer performs — use it to assert what a handler or <c>AuditingBehavior</c> recorded. Combine it with
/// <see cref="FakeUnitOfWork"/> to check that a succeeded entry is written through <c>OnBeforeCommit</c>.
/// </para>
/// <para>
/// Records every call — including when no assertion is ever made — into a thread-safe collection.
/// <see cref="ShouldHaveAudited"/> is a read-only query over that collection and never mutates it.
/// </para>
/// </remarks>
public sealed class FakeAuditTrailWriter : IAuditTrailWriter
{
    private readonly ConcurrentQueue<AuditEntry> _recorded = new();

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="RecordAsync"/> should unconditionally
    /// throw <see cref="InvalidOperationException"/> instead of recording. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Lets a test prove <c>AuditingBehavior{TRequest,TResponse}</c>'s documented fail-closed
    /// propagation — a failed audit write must propagate unchanged, never be caught or swallowed.
    /// </remarks>
    public bool SimulateFailure { get; set; }

    /// <summary>Every <see cref="AuditEntry"/> recorded via <see cref="RecordAsync"/>, in call order.</summary>
    public IReadOnlyList<AuditEntry> Recorded => [.. _recorded];

    /// <inheritdoc />
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (SimulateFailure)
        {
            throw new InvalidOperationException("FakeAuditTrailWriter was configured to simulate a failure.");
        }

        _recorded.Enqueue(entry);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asserts that an entry matching <paramref name="action"/>/<paramref name="resourceType"/>/
    /// <paramref name="resourceId"/> was recorded, and returns the first match.
    /// </summary>
    /// <exception cref="InvalidOperationException">No matching entry was recorded.</exception>
    public AuditEntry ShouldHaveAudited(string action, string resourceType, string resourceId)
    {
        foreach (var candidate in _recorded)
        {
            if (candidate.Action == action && candidate.ResourceType == resourceType && candidate.ResourceId == resourceId)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Expected an audited entry for action '{action}', resource type '{resourceType}', resource id '{resourceId}' but none was found.");
    }

    /// <summary>Clears the recorded entries only — does NOT reset <see cref="SimulateFailure"/>.</summary>
    public void Reset() => _recorded.Clear();
}
