using SharedKernel.Application.Context;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Support;

/// <summary>A minimal <see cref="IRequestContext"/> double exposing only <see cref="TenantId"/>.</summary>
internal sealed class FakeRequestContext(Guid? tenantId) : IRequestContext
{
    public bool IsAuthenticated => true;
    public string? UserId => "user-1";
    public Guid? TenantId => tenantId;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(true);
}
