using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Testing.Communication;

/// <summary>
/// In-memory test double for <see cref="IHttpContextAccessor"/> holding a fixed (or null)
/// <see cref="HttpContext"/> whose request services resolve an <see cref="IRequestContext"/> with a
/// configurable tenant.
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
    /// Builds a <see cref="FakeHttpContextAccessor"/> whose <see cref="HttpContext"/> resolves an
    /// <see cref="IRequestContext"/> for <paramref name="tenantId"/> from its <see cref="HttpContext.RequestServices"/>.
    /// </summary>
    /// <param name="tenantId">The tenant the request context reports, or <see langword="null"/> for none.</param>
    /// <returns>A new <see cref="FakeHttpContextAccessor"/>.</returns>
    public static FakeHttpContextAccessor WithTenant(TenantId? tenantId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestContext>(new SystemRequestContext([], "test", tenantId));
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        return new FakeHttpContextAccessor(context);
    }
}
