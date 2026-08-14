namespace SharedKernel.Security.Mtls.Validation;

/// <summary>
/// The outcome of validating a presented client certificate via <see cref="IMtlsCertificateValidator"/>.
/// </summary>
/// <remarks>Shape is a deliberate mirror of <c>SharedKernel.Security.ApiKey.Validation.ApiKeyValidationResult</c>.</remarks>
public sealed class MtlsValidationResult
{
    /// <summary>Gets a value indicating whether the presented certificate is valid.</summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the client identifier associated with the presented certificate, or <see langword="null"/>
    /// when <see cref="IsValid"/> is <see langword="false"/> or the validator does not track one.
    /// </summary>
    public string? ClientId { get; }

    /// <summary>
    /// Gets the roles to attach to the resulting <see cref="MtlsUserContext"/>, or <see langword="null"/>
    /// when the validator does not supply any (an optional passthrough).
    /// </summary>
    public IReadOnlyCollection<string>? Roles { get; }

    /// <summary>
    /// Gets the permissions to attach to the resulting <see cref="MtlsUserContext"/>, or
    /// <see langword="null"/> when the validator does not supply any (an optional passthrough).
    /// </summary>
    public IReadOnlyCollection<string>? Permissions { get; }

    private MtlsValidationResult(
        bool isValid,
        string? clientId,
        IReadOnlyCollection<string>? roles,
        IReadOnlyCollection<string>? permissions)
    {
        IsValid = isValid;
        ClientId = clientId;
        Roles = roles;
        Permissions = permissions;
    }

    /// <summary>Creates a successful validation result.</summary>
    /// <param name="clientId">The client identifier associated with the presented certificate, if tracked.</param>
    /// <param name="roles">Roles to attach to the resulting identity, if any.</param>
    /// <param name="permissions">Permissions to attach to the resulting identity, if any.</param>
    public static MtlsValidationResult Valid(
        string? clientId = null,
        IReadOnlyCollection<string>? roles = null,
        IReadOnlyCollection<string>? permissions = null) =>
        new(true, clientId, roles, permissions);

    /// <summary>Gets a shared, singleton failed validation result.</summary>
    public static MtlsValidationResult Invalid { get; } = new(false, null, null, null);
}
