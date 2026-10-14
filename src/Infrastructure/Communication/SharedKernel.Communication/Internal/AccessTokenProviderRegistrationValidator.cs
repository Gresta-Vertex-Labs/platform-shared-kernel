using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharedKernel.Communication.Internal;

/// <summary>
/// Fails startup when a client authenticates with <see cref="ClientAuthenticationMode.AccessTokenProvider"/> but no
/// <see cref="IAccessTokenProvider"/> is registered for it, instead of failing on the first request.
/// </summary>
/// <remarks>Reads the keyed registration without resolving it, so a provider with dependencies is not built here.</remarks>
internal sealed class AccessTokenProviderRegistrationValidator<TOptions>(string clientName, IServiceProvider services)
    : IValidateOptions<TOptions>
    where TOptions : CommunicationClientOptions
{
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        if (!string.Equals(name, clientName, StringComparison.Ordinal)
            || options.Authentication.Mode != ClientAuthenticationMode.AccessTokenProvider
            || IsRegistered())
        {
            return ValidateOptionsResult.Skip;
        }

        return ValidateOptionsResult.Fail(
            $"Client '{clientName}' authenticates with an access token provider, but none is registered. Call UseAccessTokenProvider<T>() on it.");
    }

    private bool IsRegistered() =>
        services.GetService<IServiceProviderIsKeyedService>() is { } keyed
            ? keyed.IsKeyedService(typeof(IAccessTokenProvider), clientName)
            : services.GetKeyedService<IAccessTokenProvider>(clientName) is not null;
}
