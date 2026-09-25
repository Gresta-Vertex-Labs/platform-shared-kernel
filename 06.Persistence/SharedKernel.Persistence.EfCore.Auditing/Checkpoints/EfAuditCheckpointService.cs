using SharedKernel.Execution.Context;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Auditing.Checkpoints;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Persistence.EfCore.Auditing.Writing;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// The default <see cref="IAuditCheckpointService"/>. Scope-gated (A20): a caller checkpoints its own
/// tenant's chains; any other chain needs an active cross-tenant scope. Every checkpoint is signed only
/// after the chain was verified from the latest authentic stored checkpoint to the head.
/// </summary>
internal sealed class EfAuditCheckpointService : IAuditCheckpointService
{
    private readonly AuditCheckpointWriter _writer;
    private readonly AuditCallerScope _scope;

    public EfAuditCheckpointService(AuditCheckpointWriter writer, IRequestContext requestContext, ICrossTenantScope crossTenantScope)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(crossTenantScope);

        _writer = writer;
        _scope = new AuditCallerScope(requestContext, crossTenantScope);
    }

    /// <inheritdoc />
    public Task<AuditChainCheckpoint> CreateCheckpointAsync(string resourceType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        var tenantId = _scope.ResolveCallerTenant("Creating an audit checkpoint");
        return CreateAsync(new ChainId(tenantId, resourceType), cancellationToken);
    }

    /// <inheritdoc />
    public Task<AuditChainCheckpoint> CreateCheckpointForChainAsync(Guid? tenantId, string resourceType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        _scope.RequireAccess(tenantId, nameof(CreateCheckpointForChainAsync));
        return CreateAsync(new ChainId(tenantId, resourceType), cancellationToken);
    }

    private async Task<AuditChainCheckpoint> CreateAsync(ChainId chain, CancellationToken cancellationToken) =>
        (await _writer.CreateAsync(chain, skipIfUnchanged: false, cancellationToken).ConfigureAwait(false))!;
}
