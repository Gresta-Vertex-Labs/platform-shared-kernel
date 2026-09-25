using SharedKernel.Execution.Context;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Support;

/// <summary>A minimal <see cref="IRequestContext"/> double exposing tenant and user identity.</summary>
internal sealed class FakeRequestContext(Guid? tenantId = null, string? userId = "user-1") : IRequestContext
{
    public bool IsAuthenticated => userId is not null;
    public string? UserId => userId;
    public Guid? TenantId => tenantId;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(true);
}
