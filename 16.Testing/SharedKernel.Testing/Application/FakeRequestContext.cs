using SharedKernel.Persistence.Testing;

namespace SharedKernel.Testing.Application;

/// <summary>
/// The pipeline-test flavor of <see cref="TestRequestContext"/> (<c>SharedKernel.Persistence.Testing</c>): an
/// authenticated caller with a fixed GUID-shaped user id and case-insensitive permission checks.
/// </summary>
/// <remarks>
/// <see cref="TestRequestContext.Permissions"/> defaults to empty, mirroring <c>AuthorizationBehavior</c>'s
/// fail-closed design: a permission check against an unconfigured instance returns <see langword="false"/> until the
/// test grants the permission explicitly. Never references <c>12.Security</c>.
/// </remarks>
public sealed class FakeRequestContext : TestRequestContext
{
    /// <summary>Initializes an authenticated caller with a fixed, non-empty user id and no tenant.</summary>
    public FakeRequestContext() => UserId = "11111111-1111-1111-1111-111111111111";

    /// <summary>Determines whether <see cref="TestRequestContext.Permissions"/> contains <paramref name="permission"/>.</summary>
    /// <param name="permission">The permission.</param>
    /// <returns><see langword="true"/> when granted.</returns>
    /// <remarks>Case-insensitive, mirroring <c>FakeUserContext.HasPermission</c>.</remarks>
    public override bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
