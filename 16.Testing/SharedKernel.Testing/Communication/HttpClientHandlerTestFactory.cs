namespace SharedKernel.Testing.Communication;

/// <summary>
/// Fluent builder that pre-wires a <see cref="DelegatingHandler"/> chain for direct
/// <see cref="HttpClient"/> construction in tests, without standing up a full
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public sealed class HttpClientHandlerTestFactory
{
    private HttpMessageHandler _inner = new HttpClientHandler();
    private readonly List<Func<HttpMessageHandler, HttpMessageHandler>> _wrappers = [];

    /// <summary>Sets the innermost handler (typically a <see cref="FakeHttpMessageHandler"/>).</summary>
    /// <param name="handler">The innermost handler.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public HttpClientHandlerTestFactory WithInnerHandler(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _inner = handler;
        return this;
    }

    /// <summary>
    /// Wraps the chain with a handler that injects an <c>x-correlation-id</c> header, mirroring
    /// the production <c>CorrelationIdDelegatingHandler</c> pipeline behavior.
    /// </summary>
    /// <returns>This builder, for fluent chaining.</returns>
    public HttpClientHandlerTestFactory WithCorrelationIdHandler()
    {
        _wrappers.Add(inner => new CorrelationIdHandler(inner));
        return this;
    }

    /// <summary>
    /// Wraps the chain with a handler that injects an <c>x-tenant-id</c> header when
    /// <paramref name="tenantId"/> is non-null, mirroring the production
    /// <c>TenantIdDelegatingHandler</c> pipeline behavior.
    /// </summary>
    /// <param name="tenantId">The tenant id to inject, or <see langword="null"/> to skip injection.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public HttpClientHandlerTestFactory WithTenantIdHandler(Guid? tenantId)
    {
        _wrappers.Add(inner => new TenantIdHandler(inner, tenantId));
        return this;
    }

    /// <summary>Builds the outermost handler for direct <see cref="HttpClient"/> construction.</summary>
    /// <returns>The outermost <see cref="HttpMessageHandler"/> in the configured chain.</returns>
    public HttpMessageHandler Build()
    {
        var current = _inner;
        foreach (var wrap in _wrappers)
            current = wrap(current);

        return current;
    }

    private sealed class CorrelationIdHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.Headers.Contains("x-correlation-id"))
                request.Headers.Add("x-correlation-id", Guid.NewGuid().ToString("N"));

            return base.SendAsync(request, ct);
        }
    }

    private sealed class TenantIdHandler(HttpMessageHandler inner, Guid? tenantId) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (tenantId is { } id && !request.Headers.Contains("x-tenant-id"))
                request.Headers.Add("x-tenant-id", id.ToString("D"));

            return base.SendAsync(request, ct);
        }
    }
}
