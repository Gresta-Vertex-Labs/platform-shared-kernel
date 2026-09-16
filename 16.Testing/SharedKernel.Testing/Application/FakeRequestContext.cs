using SharedKernel.Application.Context;

namespace SharedKernel.Testing.Application;

/// <summary>
/// In-memory fake implementation of <see cref="IRequestContext"/> (<c>05.Application</c>) for use in
/// unit tests.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> <c>SharedKernel.Security.Abstractions.IUserContext</c> — it fakes
/// <c>05.Application</c>'s own narrower local seam only. <see cref="IsAuthenticated"/> defaults to
/// <see langword="true"/> and <see cref="UserId"/> to a fixed, non-empty test value, mirroring
/// <see cref="SharedKernel.Testing.Security.FakeUserContext"/>'s authenticated-by-default convention
/// so most pipeline tests need zero configuration. <see cref="Permissions"/> defaults to empty —
/// deliberately the opposite default, mirroring <c>AuthorizationBehavior{TRequest,TResponse}</c>'s
/// own fail-closed design: a permission check against an unconfigured instance returns
/// <see langword="false"/> until the test grants the permission explicitly.
/// </para>
/// <para>
/// Local-seam-only scope: this type fakes <c>05.Application</c>'s own <see cref="IRequestContext"/>
/// exclusively and never references <c>12.Security</c> — bridging the local seam to a real
/// <c>SharedKernel.Security.Abstractions.IUserContext</c>/<c>ITenantProvider</c> is a decision made
/// only at each consuming service's composition root, never inside this package.
/// </para>
/// </remarks>
public sealed class FakeRequestContext : IRequestContext
{
    private static readonly string DefaultUserId = "11111111-1111-1111-1111-111111111111";

    /// <summary>Gets or sets a value indicating whether the current caller is authenticated.</summary>
    /// <remarks>Defaults to <see langword="true"/>.</remarks>
    public bool IsAuthenticated { get; set; } = true;

    /// <summary>Gets or sets the opaque subject identifier of the current caller.</summary>
    /// <remarks>Defaults to a fixed, non-empty test value.</remarks>
    public string? UserId { get; set; } = DefaultUserId;

    /// <summary>Gets or sets the tenant identifier associated with the current request.</summary>
    /// <remarks>Defaults to <see langword="null"/> — no tenant, a legitimate state for this seam.</remarks>
    public Guid? TenantId { get; set; }

    /// <summary>Gets or sets the permissions held by the current caller.</summary>
    /// <remarks>Defaults to an empty collection. Mirrors <c>FakeUserContext.Permissions</c>'s exact shape.</remarks>
    public IReadOnlyCollection<string> Permissions { get; set; } = [];

    /// <summary>Determines whether <see cref="Permissions"/> contains <paramref name="permission"/>.</summary>
    /// <remarks>Comparison is case-insensitive, mirroring <c>FakeUserContext.HasPermission</c>.</remarks>
    public bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return ValueTask.FromResult(HasPermission(permission));
    }
}
