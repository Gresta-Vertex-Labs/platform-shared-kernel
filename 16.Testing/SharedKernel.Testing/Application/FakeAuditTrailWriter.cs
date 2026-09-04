using System.Collections.Concurrent;
using SharedKernel.Application.Behaviors.Auditing;

namespace SharedKernel.Testing.Application;

/// <summary>
/// In-memory fake implementation of <see cref="SharedKernel.Application.Behaviors.Auditing.IAuditTrailWriter"/>
/// (<c>05.Application.Behaviors</c>) for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>NOT</b> <see cref="SharedKernel.Testing.Persistence.FakeAuditTrailWriter"/> — same class name,
/// different namespace, mirroring the already-shipped <c>FakeUnitOfWork</c>/<c>FakeUnitOfWork</c>
/// naming-collision precedent exactly (<c>SharedKernel.Testing.Application</c> vs.
/// <c>SharedKernel.Testing.Persistence</c>). This type fakes <c>05.Application.Behaviors</c>'s OWN,
/// deliberately smaller local-seam <see cref="IAuditTrailWriter"/> — a caller-supplied
/// <see cref="AuditEntry"/> recorded verbatim, with none of the richer resolution
/// (<c>Id</c>/<c>ActorId</c>/<c>TenantId</c>/<c>OccurredOn</c>/hash-chaining) the REAL
/// <c>06.Persistence.Abstractions.IAuditTrailWriter</c> performs — see
/// <see cref="SharedKernel.Testing.Persistence.FakeAuditTrailWriter"/> for that fake.
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
