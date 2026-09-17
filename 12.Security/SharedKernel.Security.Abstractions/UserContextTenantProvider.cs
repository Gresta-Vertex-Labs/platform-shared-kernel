namespace SharedKernel.Security.Abstractions;

/// <summary>An <see cref="ITenantProvider"/> that returns the tenant asserted by the caller's credential.</summary>
/// <param name="userContext">The caller.</param>
public sealed class UserContextTenantProvider(IUserContext userContext) : ITenantProvider
{
    private readonly IUserContext _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));

    /// <inheritdoc/>
    public Guid TenantId => _userContext.TenantId ?? Guid.Empty;
}
