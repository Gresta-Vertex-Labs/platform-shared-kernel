using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Security.ApiKey.Validation;

/// <summary>The outcome of validating an API key.</summary>
public sealed class ApiKeyValidationResult
{
    private ApiKeyValidationResult(
        bool isValid,
        string? clientId,
        TenantId? tenantId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        string? keyId,
        string? failureReason)
    {
        IsValid = isValid;
        ClientId = clientId;
        TenantId = tenantId;
        Roles = roles;
        Permissions = permissions;
        KeyId = keyId;
        FailureReason = failureReason;
    }

    /// <summary>Gets a value indicating whether the key is valid.</summary>
    public bool IsValid { get; }

    /// <summary>Gets the client the key belongs to; the caller's subject id. <see langword="null"/> on failure.</summary>
    public string? ClientId { get; }

    /// <summary>Gets the tenant the key is limited to, or <see langword="null"/>.</summary>
    public TenantId? TenantId { get; }

    /// <summary>Gets the roles granted to the key.</summary>
    public IReadOnlyCollection<string> Roles { get; }

    /// <summary>Gets the permissions granted to the key.</summary>
    public IReadOnlyCollection<string> Permissions { get; }

    /// <summary>Gets the non-secret key id, for audit logs, or <see langword="null"/>.</summary>
    public string? KeyId { get; }

    /// <summary>Gets a short, non-secret reason for a failure, for logs only. <see langword="null"/> on success.</summary>
    public string? FailureReason { get; }

    /// <summary>Creates a successful result.</summary>
    /// <param name="clientId">The client the key belongs to. Must not be empty.</param>
    /// <param name="tenantId">The tenant the key is limited to, if any. Must not be <c>default(TenantId)</c>.</param>
    /// <param name="roles">The roles granted to the key.</param>
    /// <param name="permissions">The permissions granted to the key.</param>
    /// <param name="keyId">The non-secret key id, if any.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException"><paramref name="clientId"/> is empty or <paramref name="tenantId"/> is <c>default(TenantId)</c>.</exception>
    public static ApiKeyValidationResult Success(
        string clientId,
        TenantId? tenantId = null,
        IReadOnlyCollection<string>? roles = null,
        IReadOnlyCollection<string>? permissions = null,
        string? keyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        if (tenantId is { IsDefault: true })
        {
            throw new ArgumentException("A tenant id must not be default(TenantId); pass null for no tenant.", nameof(tenantId));
        }

        return new ApiKeyValidationResult(true, clientId, tenantId, roles ?? [], permissions ?? [], keyId, null);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="reason">A short, non-secret reason for logs, such as <c>Expired</c>. Never included in the response.</param>
    /// <param name="keyId">The non-secret key id, when known.</param>
    /// <returns>The result.</returns>
    public static ApiKeyValidationResult Failure(string reason = "Rejected", string? keyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ApiKeyValidationResult(false, null, null, [], [], keyId, reason);
    }
}
