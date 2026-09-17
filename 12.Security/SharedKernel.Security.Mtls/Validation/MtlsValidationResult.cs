namespace SharedKernel.Security.Mtls.Validation;

/// <summary>The outcome of validating a client certificate.</summary>
public sealed class MtlsValidationResult
{
    private MtlsValidationResult(
        bool isValid,
        string? clientId,
        Guid? tenantId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        string? failureReason)
    {
        IsValid = isValid;
        ClientId = clientId;
        TenantId = tenantId;
        Roles = roles;
        Permissions = permissions;
        FailureReason = failureReason;
    }

    /// <summary>Gets a value indicating whether the certificate is accepted.</summary>
    public bool IsValid { get; }

    /// <summary>Gets the client the certificate belongs to; the caller's subject id. <see langword="null"/> on failure.</summary>
    public string? ClientId { get; }

    /// <summary>Gets the tenant the client is limited to, or <see langword="null"/>.</summary>
    public Guid? TenantId { get; }

    /// <summary>Gets the roles granted to the client.</summary>
    public IReadOnlyCollection<string> Roles { get; }

    /// <summary>Gets the permissions granted to the client.</summary>
    public IReadOnlyCollection<string> Permissions { get; }

    /// <summary>Gets a short, non-secret reason for a failure, for logs only. <see langword="null"/> on success.</summary>
    public string? FailureReason { get; }

    /// <summary>Creates a successful result.</summary>
    /// <param name="clientId">The client the certificate belongs to. Must not be empty.</param>
    /// <param name="tenantId">The tenant the client is limited to, if any. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="roles">The roles granted to the client.</param>
    /// <param name="permissions">The permissions granted to the client.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException"><paramref name="clientId"/> is empty or <paramref name="tenantId"/> is <see cref="Guid.Empty"/>.</exception>
    public static MtlsValidationResult Success(
        string clientId,
        Guid? tenantId = null,
        IReadOnlyCollection<string>? roles = null,
        IReadOnlyCollection<string>? permissions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant id must not be Guid.Empty; pass null for no tenant.", nameof(tenantId));
        }

        return new MtlsValidationResult(true, clientId, tenantId, roles ?? [], permissions ?? [], null);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="reason">A short, non-secret reason for logs, such as <c>UnknownClient</c>.</param>
    /// <returns>The result.</returns>
    public static MtlsValidationResult Failure(string reason = "Rejected")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new MtlsValidationResult(false, null, null, [], [], reason);
    }
}
