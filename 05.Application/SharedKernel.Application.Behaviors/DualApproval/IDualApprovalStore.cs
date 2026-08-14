namespace SharedKernel.Application.Behaviors.DualApproval;

/// <summary>
/// A minimal seam for recording and querying dual-control ("maker-checker") approval records.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="Idempotency.IIdempotencyKeyStore"/>'s bridge shape exactly — this package
/// ships only the interface, no implementation; the consuming service provides one backed by
/// whatever durable store it prefers (a dedicated approvals table, a distributed cache key, etc.)
/// and registers it at the composition root. Zero reference to
/// <c>06.Persistence</c>/<c>07.Messaging</c>/<c>12.Security</c> from this package — same layering
/// discipline as every other local seam in this domain.
/// </para>
/// <para>
/// <see cref="RecordApprovalAsync"/> is called ONLY by the consuming service's own separate
/// approval-recording workflow (e.g. a distinct "ApproveChangeCommand" an approver dispatches
/// through their own request) — the pipeline behavior that reads approval state
/// (<c>DualApprovalBehavior&lt;TRequest,TResponse&gt;</c>) never calls this method itself; it only
/// ever reads via <see cref="TryGetApprovalAsync"/>.
/// </para>
/// </remarks>
public interface IDualApprovalStore
{
    /// <summary>
    /// Attempts to retrieve the identity of the approver who recorded an approval for
    /// <paramref name="approvalKey"/>.
    /// </summary>
    /// <param name="approvalKey">The approval key supplied by the command instance.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// The recorded approver's opaque identity string if an approval has been recorded for this
    /// key; otherwise <see langword="null"/> if no approval has been recorded yet.
    /// </returns>
    /// <remarks>
    /// The identity itself (not just a <see cref="bool"/>) is required so the caller can compare
    /// it against the current initiator's identity for self-approval prevention.
    /// </remarks>
    Task<string?> TryGetApprovalAsync(string approvalKey, CancellationToken cancellationToken);

    /// <summary>Records <paramref name="approverIdentity"/> as having approved <paramref name="approvalKey"/>.</summary>
    /// <param name="approvalKey">The approval key supplied by the command instance.</param>
    /// <param name="approverIdentity">
    /// The opaque identity of the approver, consistent with the identity representation the
    /// consuming service's composition-root bridge uses elsewhere (e.g. the same identity string
    /// exposed via the authorization seam's identity-resolution capability).
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task RecordApprovalAsync(string approvalKey, string approverIdentity, CancellationToken cancellationToken);
}
