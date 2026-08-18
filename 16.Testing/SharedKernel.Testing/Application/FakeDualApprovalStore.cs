using System.Collections.Concurrent;
using SharedKernel.Application.Behaviors.DualApproval;

namespace SharedKernel.Testing.Application;

/// <summary>
/// In-memory fake implementation of <see cref="IDualApprovalStore"/> (<c>05.Application.Behaviors</c>)
/// for use in unit tests.
/// </summary>
/// <remarks>
/// Mirrors <see cref="FakeIdempotencyKeyStore"/>/<see cref="FakeIdempotencyResponseStore"/>'s exact
/// local-seam-fake precedent — this package ships a fake for a <c>05.Application.Behaviors</c>-owned
/// local interface, never a cross-domain <c>12.Security</c>/<c>06.Persistence</c>/<c>07.Messaging</c>
/// type. Backed by a thread-safe <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by approval key.
/// No <c>.Seed(...)</c> alias — <see cref="RecordApprovalAsync"/> already IS the seeding mechanism per
/// the real contract's own documented single-writer-path design.
/// </remarks>
public sealed class FakeDualApprovalStore : IDualApprovalStore
{
    private readonly ConcurrentDictionary<string, string> _approvals = new();

    /// <summary>
    /// Gets or sets a value indicating whether both members throw <see cref="InvalidOperationException"/>
    /// instead of completing normally.
    /// </summary>
    /// <remarks>Defaults to <see langword="false"/>.</remarks>
    public bool SimulateFailure { get; set; }

    /// <inheritdoc />
    public Task<string?> TryGetApprovalAsync(string approvalKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approvalKey);

        if (SimulateFailure)
        {
            throw new InvalidOperationException("Simulated IDualApprovalStore failure.");
        }

        return Task.FromResult(_approvals.TryGetValue(approvalKey, out var approver) ? approver : null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Upserts — lets a test play the role of the consuming service's own separate approval-recording
    /// workflow, since <c>DualApprovalBehavior</c> itself never calls this member (it only ever reads).
    /// </remarks>
    public Task RecordApprovalAsync(string approvalKey, string approverIdentity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approvalKey);
        ArgumentNullException.ThrowIfNull(approverIdentity);

        if (SimulateFailure)
        {
            throw new InvalidOperationException("Simulated IDualApprovalStore failure.");
        }

        _approvals[approvalKey] = approverIdentity;
        return Task.CompletedTask;
    }

    /// <summary>Clears the recorded approval store only — does NOT reset <see cref="SimulateFailure"/>.</summary>
    public void Reset() => _approvals.Clear();
}
