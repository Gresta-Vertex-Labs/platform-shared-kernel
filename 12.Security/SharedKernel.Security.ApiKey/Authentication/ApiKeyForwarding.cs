using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SharedKernel.Security.ApiKey.Authentication;

// Makes the forwarding scheme the default scheme and remembers the default it replaced, whichever order the
// authentication packages were registered in.
internal sealed class ApiKeyForwarding : IPostConfigureOptions<AuthenticationOptions>
{
    public string? FallbackScheme { get; private set; }

    public void PostConfigure(string? name, AuthenticationOptions options)
    {
        if (options.DefaultScheme is { } current && current != ApiKeyAuthenticationDefaults.ForwardingScheme)
        {
            FallbackScheme = current;
        }

        options.DefaultScheme = ApiKeyAuthenticationDefaults.ForwardingScheme;
    }
}
