namespace SharedKernel.Communication.Internal.Resolvers;

/// <summary>
/// Resolves a logical service name to a routable <see cref="Uri"/> using the cluster's
/// service-discovery mechanism.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never-throw contract (production):</b> Implementations must never throw for an unresolvable
/// service name. On any resolution failure the implementation returns a <see cref="Uri"/> built
/// from the K8s DNS convention (<c>{scheme}://{serviceName}.{namespace}.svc.{clusterDomain}</c>)
/// and lets the caller's transport layer surface the connection error.
/// </para>
/// <para>
/// Callers should inject <see cref="IServiceEndpointResolver"/> and pass the result to typed
/// client constructors — never construct <see cref="Uri"/> values from configuration strings
/// directly inside a client method.
/// </para>
/// </remarks>
public interface IServiceEndpointResolver
{
    /// <summary>
    /// Resolves <paramref name="serviceName"/> to a base <see cref="Uri"/>.
    /// </summary>
    /// <param name="serviceName">Logical service name (e.g. <c>"order-service"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A non-null <see cref="Uri"/>. Returns the K8s DNS-convention URI on resolution failure
    /// — never throws.
    /// </returns>
    ValueTask<Uri> ResolveAsync(string serviceName, CancellationToken ct);
}
