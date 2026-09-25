using SharedKernel.Execution.Context;
using SharedKernel.Messaging.Abstractions.Context;

namespace SharedKernel.Messaging.MassTransit.Context;

/// <summary>
/// The <c>IRequestContext</c> a service resolves once
/// <c>MessagingBusBuilder.WithInboundRequestContext()</c> is enabled: the message's caller inside a
/// consume, the service's own caller everywhere else.
/// </summary>
/// <remarks>
/// <para>
/// This exists so that one handler works on both paths. A command handler invoked from an HTTP
/// endpoint and from a consumer injects <c>IRequestContext</c> once and gets the right answer in
/// both, instead of branching on how it was reached.
/// </para>
/// <para>
/// Every member is forwarded per call rather than captured in the constructor, because the inbound
/// identity is written by a consume filter that runs after this object may already have been
/// resolved.
/// </para>
/// </remarks>
internal sealed class MessageAwareRequestContext : IRequestContext
{
    private readonly IInboundMessageContextAccessor _inbound;
    private readonly HostRequestContextSource? _host;

    /// <summary>Initialises the composite.</summary>
    /// <param name="inbound">The delivery scope's inbound identity holder.</param>
    /// <param name="host">
    /// The service's own request context, or <see langword="null"/> when it had none registered —
    /// in which case the non-consume answer is <see cref="AnonymousRequestContext"/>, the same
    /// fail-closed default persistence already applies.
    /// </param>
    public MessageAwareRequestContext(
        IInboundMessageContextAccessor inbound,
        HostRequestContextSource? host = null)
    {
        _inbound = inbound;
        _host = host;
    }

    private IRequestContext Current
        => _inbound.Current ?? _host?.RequestContext ?? AnonymousRequestContext.Instance;

    /// <inheritdoc />
    public bool IsAuthenticated => Current.IsAuthenticated;

    /// <inheritdoc />
    public string? UserId => Current.UserId;

    /// <inheritdoc />
    public Guid? TenantId => Current.TenantId;

    /// <inheritdoc />
    public ActorKind ActorKind => Current.ActorKind;

    /// <inheritdoc />
    public string? ClientId => Current.ClientId;

    /// <inheritdoc />
    public string? SessionId => Current.SessionId;

    /// <inheritdoc />
    public string? ImpersonatorId => Current.ImpersonatorId;

    /// <inheritdoc />
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => Current.HasPermissionAsync(permission, cancellationToken);
}
