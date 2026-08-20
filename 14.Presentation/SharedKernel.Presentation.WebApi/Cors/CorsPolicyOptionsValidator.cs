using Microsoft.Extensions.Options;

namespace SharedKernel.Presentation.WebApi.Cors;

/// <summary>
/// Fails startup validation when <see cref="CorsPolicyOptions.AllowCredentials"/> is combined with
/// an empty or wildcard <see cref="CorsPolicyOptions.AllowedOrigins"/> list.
/// </summary>
/// <remarks>
/// This is the classic OWASP-catalogued misconfiguration the CORS specification itself forbids, but
/// that ASP.NET Core's own CORS middleware only throws on at the FIRST real credentialed
/// cross-origin request in production. Registered via
/// <see cref="CorsExtensions.AddSharedKernelCors"/> with <c>ValidateOnStart()</c>, so the
/// combination fails fast at <c>IHost.StartAsync()</c> instead.
/// </remarks>
internal sealed class CorsPolicyOptionsValidator : IValidateOptions<CorsPolicyOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, CorsPolicyOptions options)
    {
        var isEmptyOrWildcard = options.AllowedOrigins.Count == 0
            || options.AllowedOrigins.Any(origin => origin == "*");

        if (options.AllowCredentials && isEmptyOrWildcard)
        {
            return ValidateOptionsResult.Fail(
                "CorsPolicyOptions.AllowCredentials cannot be combined with an empty or wildcard "
                    + "AllowedOrigins list. Specify one or more explicit origins, or set "
                    + "AllowCredentials to false.");
        }

        return ValidateOptionsResult.Success;
    }
}
