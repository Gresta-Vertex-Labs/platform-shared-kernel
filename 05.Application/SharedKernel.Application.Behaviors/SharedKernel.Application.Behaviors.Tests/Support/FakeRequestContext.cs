using SharedKernel.Application.Context;

namespace SharedKernel.Application.Behaviors.Tests.Support;

/// <summary>A minimal <see cref="IRequestContext"/> double driven by caller-supplied values.</summary>
internal sealed class FakeRequestContext(
    bool isAuthenticated,
    ISet<string>? grantedPermissions = null) : IRequestContext
{
    public bool IsAuthenticated => isAuthenticated;

    public string? UserId => isAuthenticated ? "user-1" : null;

    public Guid? TenantId { get; set; }

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(grantedPermissions?.Contains(permission) ?? false);
}
