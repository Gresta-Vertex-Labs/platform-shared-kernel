using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Thrown when an operation that must only run over an intact chain (creating a checkpoint) finds the
/// chain broken or unverifiable. Nothing is signed.
/// </summary>
public sealed class AuditChainIntegrityException : SharedKernelException
{
    /// <summary>The error code carried by <see cref="SharedKernelException.Error"/>.</summary>
    public const string ErrorCode = "persistence.audit_chain_not_intact";

    /// <summary>Initialises a new <see cref="AuditChainIntegrityException"/>.</summary>
    /// <param name="tenantId">The chain's tenant.</param>
    /// <param name="resourceType">The chain's resource type.</param>
    /// <param name="verification">The verification that failed.</param>
    public AuditChainIntegrityException(Guid? tenantId, string resourceType, AuditChainVerificationResult verification)
        : base(BuildMessage(tenantId, resourceType, verification), Error.Conflict(ErrorCode, BuildMessage(tenantId, resourceType, verification)))
    {
        ArgumentNullException.ThrowIfNull(verification);
        TenantId = tenantId;
        ResourceType = resourceType;
        Verification = verification;
    }

    /// <summary>Gets the chain's tenant, or <see langword="null"/> for the system chain.</summary>
    public Guid? TenantId { get; }

    /// <summary>Gets the chain's resource type.</summary>
    public string ResourceType { get; }

    /// <summary>Gets the verification that failed.</summary>
    public AuditChainVerificationResult Verification { get; }

    private static string BuildMessage(Guid? tenantId, string resourceType, AuditChainVerificationResult verification) =>
        $"Audit chain ({tenantId?.ToString("D") ?? "system"}, '{resourceType}') is {verification?.Status} " +
        $"({verification?.FailureKind} at sequence {verification?.FailedAtSequence}): {verification?.Reason}";
}
