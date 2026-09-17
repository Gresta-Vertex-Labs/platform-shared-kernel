using SharedKernel.Security.Abstractions;

namespace SharedKernel.Testing.Security;

/// <summary>
/// In-memory fake implementation of <see cref="ITenantProvider"/> for use in unit tests.
/// </summary>
/// <remarks>
/// Defaults to a fixed, non-empty test <see cref="Guid"/> — <em>not</em> <see cref="Guid.Empty"/> —
/// so tenant-scoped code under test exercises the tenanted path by default. Set
/// <see cref="TenantId"/> to <see cref="Guid.Empty"/> explicitly to test the no-tenant-resolved
/// path.
/// </remarks>
public sealed class FakeTenantProvider : ITenantProvider
{
    private static readonly Guid DefaultTenantId = new("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Initialises a new <see cref="FakeTenantProvider"/>.
    /// </summary>
    /// <param name="tenantId">
    /// The tenant id to use. Defaults to a fixed, non-empty test <see cref="Guid"/> when omitted —
    /// never <see cref="Guid.Empty"/>. Pass <see cref="Guid.Empty"/> explicitly to simulate the
    /// no-tenant-resolved case.
    /// </param>
    public FakeTenantProvider(Guid? tenantId = null) => TenantId = tenantId ?? DefaultTenantId;

    /// <summary>
    /// Gets or sets the tenant identifier for the current request.
    /// </summary>
    public Guid TenantId { get; set; }
}
