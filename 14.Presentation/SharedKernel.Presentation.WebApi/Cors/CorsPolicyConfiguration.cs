using Microsoft.AspNetCore.Cors.Infrastructure;

namespace SharedKernel.Presentation.WebApi.Cors;

/// <summary>Builds the one CORS policy <c>UseSharedKernelWebApi()</c> applies to every endpoint.</summary>
internal static class CorsPolicyConfiguration
{
    /// <summary>The name of the policy; applied globally, so no endpoint names it.</summary>
    public const string PolicyName = "SharedKernel.Presentation.WebApi";

    /// <summary>Returns <see langword="true"/> when at least one origin is allowed, the only case with a policy.</summary>
    public static bool IsEnabled(WebApiCorsOptions options) => options.AllowedOrigins.Count > 0;

    /// <summary>Adds the policy to <paramref name="cors"/> when origins are configured.</summary>
    public static void Configure(CorsOptions cors, WebApiCorsOptions options)
    {
        if (!IsEnabled(options))
        {
            return;
        }

        cors.AddPolicy(PolicyName, policy =>
        {
            if (options.AllowedOrigins.Contains(WebApiOptionsValidator.WildcardOrigin))
            {
                policy.AllowAnyOrigin();
            }
            else
            {
                policy.WithOrigins([.. options.AllowedOrigins]);
            }

            if (options.AllowedMethods.Count > 0)
            {
                policy.WithMethods([.. options.AllowedMethods]);
            }
            else
            {
                policy.AllowAnyMethod();
            }

            if (options.AllowedHeaders.Count > 0)
            {
                policy.WithHeaders([.. options.AllowedHeaders]);
            }
            else
            {
                policy.AllowAnyHeader();
            }

            if (options.ExposedHeaders.Count > 0)
            {
                policy.WithExposedHeaders([.. options.ExposedHeaders.Distinct(StringComparer.OrdinalIgnoreCase)]);
            }

            if (options.AllowCredentials)
            {
                policy.AllowCredentials();
            }

            policy.SetPreflightMaxAge(options.PreflightMaxAge);
        });
    }
}
