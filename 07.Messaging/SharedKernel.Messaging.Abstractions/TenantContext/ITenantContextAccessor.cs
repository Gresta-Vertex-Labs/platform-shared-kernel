namespace SharedKernel.Messaging.Abstractions.TenantContext;

/// <summary>
/// Locally-owned seam for resolving the ambient tenant identity at message publish time.
/// </summary>
/// <remarks>
/// <para>
/// This interface references nothing outside <c>01.Core</c>–<c>04.Contracts</c> — it mirrors
/// <c>05.Application</c>'s <c>IRequestContext</c>/<c>IUnitOfWork</c> bridge pattern.
/// The consuming service's composition root implements this interface against its own real
/// tenant-identity source (e.g. <c>ITenantProvider</c> from <c>12.Security</c>, or
/// <c>ICurrentTenantService</c> from <c>06.Persistence</c>). <c>SharedKernel.Messaging.*</c> never
/// references either of those packages directly (P-345/WO-054).
/// </para>
/// <para>
/// Register the implementation and enable automatic tenant propagation onto every published
/// integration event via
/// <c>MessagingBusBuilder.WithTenantContext&lt;TAccessor&gt;()</c>. When no implementation is
/// registered, <c>TenantHeaderPropagator</c> — the built-in <see cref="Abstractions.HeaderPropagation.IMessageHeaderPropagator"/>
/// this seam feeds — is a provable no-op.
/// </para>
/// <para>
/// <strong>Example</strong> (bridging an ASP.NET Core <c>ITenantProvider</c>):
/// </para>
/// <code>
/// public sealed class HttpTenantContextAccessor : ITenantContextAccessor
/// {
///     private readonly ITenantProvider _tenantProvider;
///     public HttpTenantContextAccessor(ITenantProvider tenantProvider)
///         => _tenantProvider = tenantProvider;
///
///     public Guid? TenantId => _tenantProvider.TenantId;
/// }
/// </code>
/// </remarks>
public interface ITenantContextAccessor
{
    /// <summary>
    /// Gets the current ambient tenant identity, or <see langword="null"/> when no tenant context
    /// is available (e.g. an unauthenticated request, or a background job with no tenant scope).
    /// </summary>
    Guid? TenantId { get; }
}
