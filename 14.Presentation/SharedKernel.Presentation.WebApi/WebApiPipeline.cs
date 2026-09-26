using Microsoft.AspNetCore.Builder;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// The places in the pipeline of <see cref="WebApiApplicationBuilderExtensions.UseSharedKernelWebApi"/> where a
/// service adds its own middleware, for the few that must run inside it rather than after it.
/// </summary>
/// <remarks>
/// <para>Each hook runs its registrations in the order they were added:</para>
/// <list type="bullet">
///   <item><see cref="AtStart"/>: before everything else in this pipeline — forwarded headers,
///   so HSTS, the scheme and the client address are right from the first middleware on.</item>
///   <item><see cref="BeforeAuthentication"/>: after routing and CORS, before authentication — certificate forwarding.</item>
///   <item><see cref="BeforeAuthorization"/>: after authentication, before rate limiting and authorization — request
///   localization, so 401, 403 and 429 responses are written in the caller's language.</item>
/// </list>
/// <para>Middleware that needs none of these positions goes after <c>UseSharedKernelWebApi()</c>.</para>
/// </remarks>
public sealed class WebApiPipeline
{
    private readonly List<Action<IApplicationBuilder>> _atStart = [];
    private readonly List<Action<IApplicationBuilder>> _beforeAuthentication = [];
    private readonly List<Action<IApplicationBuilder>> _beforeAuthorization = [];

    internal WebApiPipeline()
    {
    }

    /// <summary>
    /// Adds middleware at the start of the pipeline, before HSTS, security headers and the exception handler
    /// (<c>UseSharedKernelRequestContext()</c> runs before this whole pipeline) — for example
    /// <c>app =&gt; app.UseForwardedHeaders()</c>.
    /// </summary>
    /// <param name="configure">Adds the middleware to the application builder it is given.</param>
    /// <returns>This instance.</returns>
    /// <remarks>An exception thrown by this middleware is not turned into a problem response.</remarks>
    public WebApiPipeline AtStart(Action<IApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _atStart.Add(configure);
        return this;
    }

    /// <summary>
    /// Adds middleware after routing and CORS and before authentication — for example
    /// <c>app =&gt; app.UseCertificateForwarding()</c>.
    /// </summary>
    /// <param name="configure">Adds the middleware to the application builder it is given.</param>
    /// <returns>This instance.</returns>
    public WebApiPipeline BeforeAuthentication(Action<IApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _beforeAuthentication.Add(configure);
        return this;
    }

    /// <summary>
    /// Adds middleware after authentication and before rate limiting and authorization — for example
    /// <c>app =&gt; app.UseRequestLocalization()</c>, which can then read the signed-in user and translates refusals.
    /// </summary>
    /// <param name="configure">Adds the middleware to the application builder it is given.</param>
    /// <returns>This instance.</returns>
    public WebApiPipeline BeforeAuthorization(Action<IApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _beforeAuthorization.Add(configure);
        return this;
    }

    internal void ApplyAtStart(IApplicationBuilder app) => Apply(_atStart, app);

    internal void ApplyBeforeAuthentication(IApplicationBuilder app) => Apply(_beforeAuthentication, app);

    internal void ApplyBeforeAuthorization(IApplicationBuilder app) => Apply(_beforeAuthorization, app);

    private static void Apply(List<Action<IApplicationBuilder>> hooks, IApplicationBuilder app)
    {
        foreach (var hook in hooks)
        {
            hook(app);
        }
    }
}
