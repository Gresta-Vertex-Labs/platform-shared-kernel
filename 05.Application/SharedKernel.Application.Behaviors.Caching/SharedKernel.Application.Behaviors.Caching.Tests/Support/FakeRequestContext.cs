using SharedKernel.Execution.Context;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Support;

/// <summary>A minimal <see cref="IRequestContext"/> double exposing tenant and user identity.</summary>
internal sealed class FakeRequestContext(TenantId? tenantId = null, string? userId = "user-1") : IRequestContext
{
    public bool IsAuthenticated => userId is not null;
    public string? UserId => userId;
    public TenantId? TenantId => tenantId;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(true);
}
