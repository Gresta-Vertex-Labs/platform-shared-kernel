using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Thrown when an operation that must only run over an intact chain (creating a checkpoint) finds the
/// chain broken or unverifiable. Nothing is signed.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> and <c>Error.Message</c> name the resource type, status, failure kind and sequence only;
/// the chain's tenant is available through <see cref="TenantId"/> and the reason through <see cref="Verification"/>,
/// and neither is part of the text. The error code is <see cref="PersistenceErrorCodes.Audit.ChainNotIntact"/>.
/// </remarks>
public sealed class AuditChainIntegrityException : SharedKernelException
{
    /// <summary>Initialises a new <see cref="AuditChainIntegrityException"/>.</summary>
    /// <param name="tenantId">The chain's tenant.</param>
    /// <param name="resourceType">The chain's resource type.</param>
    /// <param name="verification">The verification that failed.</param>
    public AuditChainIntegrityException(TenantId? tenantId, string resourceType, AuditChainVerificationResult verification)
        : base(Error.Conflict(PersistenceErrorCodes.Audit.ChainNotIntact, BuildMessage(resourceType, verification)))
    {
        ArgumentNullException.ThrowIfNull(verification);
        TenantId = tenantId;
        ResourceType = resourceType;
        Verification = verification;
    }

    /// <summary>Gets the chain's tenant, or <see langword="null"/> for the system chain.</summary>
    public TenantId? TenantId { get; }

    /// <summary>Gets the chain's resource type.</summary>
    public string ResourceType { get; }

    /// <summary>Gets the verification that failed.</summary>
    public AuditChainVerificationResult Verification { get; }

    // The tenant and the reason are properties for programmatic use, never part of the text: the message reaches ProblemDetails.
    private static string BuildMessage(string resourceType, AuditChainVerificationResult verification) =>
        $"The audit chain of resource type '{resourceType}' is {verification?.Status} " +
        $"({verification?.FailureKind} at sequence {verification?.FailedAtSequence}). Verify the chain and restore it from a backup before checkpointing.";
}
