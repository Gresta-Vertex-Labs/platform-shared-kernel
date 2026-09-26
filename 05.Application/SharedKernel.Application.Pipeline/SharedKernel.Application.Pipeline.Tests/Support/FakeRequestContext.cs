using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Application.Pipeline.Tests.Support;

/// <summary>A minimal <see cref="IRequestContext"/> double driven by caller-supplied values.</summary>
/// <remarks>
/// Authenticated instances default to user <c>user-1</c> of kind <see cref="ActorKind.User"/>; unauthenticated ones to
/// no subject and <see cref="ActorKind.Anonymous"/>. Every identity member is settable, so a test can act as another
/// caller.
/// </remarks>
internal sealed class FakeRequestContext(
    bool isAuthenticated,
    ISet<string>? grantedPermissions = null) : IRequestContext
{
    public bool IsAuthenticated => isAuthenticated;

    public string? UserId { get; set; } = isAuthenticated ? "user-1" : null;

    public TenantId? TenantId { get; set; }

    public ActorKind ActorKind { get; set; } = isAuthenticated ? ActorKind.User : ActorKind.Anonymous;

    public string? ClientId { get; set; }

    public string? SessionId { get; set; }

    public string? ImpersonatorId { get; set; }

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(grantedPermissions?.Contains(permission) ?? false);
}
