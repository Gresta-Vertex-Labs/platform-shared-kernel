using SharedKernel.Application.Context;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

/// <summary>Mutable, scoped-DI-friendly actor fake for pooling tests.</summary>
internal sealed class MutableTestActorContext : IRequestContext
{
    public string ActorId { get; set; } = "unset";
    public ActorKind ActorKind { get; set; } = ActorKind.User;
    public bool IsAuthenticated => true;
    public string? UserId => ActorId;
    public Guid? TenantId { get; set; }

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}

/// <summary>Mutable, scoped-DI-friendly tenant fake for pooling tests.</summary>
internal sealed class MutableTestTenantContext : IRequestContext
{
    public Guid? TenantId { get; set; }
    public bool IsAuthenticated => true;
    public string? UserId => "tenant-test";

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}
