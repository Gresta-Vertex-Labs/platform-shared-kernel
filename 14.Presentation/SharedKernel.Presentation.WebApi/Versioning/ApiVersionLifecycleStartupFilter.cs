using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace SharedKernel.Presentation.WebApi.Versioning;

/// <summary>
/// Self-inserts <see cref="ApiVersionLifecycleMiddleware"/> into the request pipeline.
/// </summary>
/// <remarks>
/// <c>AddSharedKernelApiVersioning</c> gains its RFC 8594 sunset/deprecation support via an
/// additive parameter on the existing method — never a second <c>Use...</c> registration method.
/// An <see cref="IStartupFilter"/> is the standard ASP.NET Core mechanism for a library to insert
/// middleware without requiring the host to call anything, mirroring the zero-extra-wiring
/// experience <c>ReportApiVersions</c>'s own <c>api-supported-versions</c> header already provides.
/// </remarks>
internal sealed class ApiVersionLifecycleStartupFilter : IStartupFilter
{
    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.UseMiddleware<ApiVersionLifecycleMiddleware>();
            next(app);
        };
}
