using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Testing.Communication;

/// <summary>
/// In-memory test double for <see cref="IHttpContextAccessor"/> holding a fixed (or null)
/// <see cref="HttpContext"/> with a configurable <see cref="ITenantProvider"/>.
/// </summary>
/// <remarks>
/// Consolidates ad-hoc duplicate fakes previously hand-rolled in
/// <c>SharedKernel.Communication.Rest.Tests</c> and <c>.Grpc.Tests</c>.
/// </remarks>
public sealed class FakeHttpContextAccessor : IHttpContextAccessor
{
    /// <summary>
    /// Initialises a new <see cref="FakeHttpContextAccessor"/> wrapping the supplied context.
    /// </summary>
    /// <param name="context">
    /// The fixed <see cref="HttpContext"/> to expose, or <see langword="null"/> to simulate the
    /// no-active-request case.
    /// </param>
    public FakeHttpContextAccessor(HttpContext? context = null) => HttpContext = context;

    /// <inheritdoc />
    public HttpContext? HttpContext { get; set; }

    /// <summary>
    /// Builds a <see cref="FakeHttpContextAccessor"/> whose <see cref="HttpContext"/> resolves a
    /// fixed <see cref="ITenantProvider"/> from its <see cref="HttpContext.RequestServices"/>.
    /// </summary>
    /// <param name="tenantId">The tenant id the backing <see cref="ITenantProvider"/> returns.</param>
    /// <returns>A new <see cref="FakeHttpContextAccessor"/>.</returns>
    public static FakeHttpContextAccessor WithTenant(Guid tenantId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantProvider>(new FixedTenantProvider(tenantId));
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        return new FakeHttpContextAccessor(context);
    }

    private sealed class FixedTenantProvider(Guid tenantId) : ITenantProvider
    {
        public Guid TenantId => tenantId;
    }
}
