using System.Buffers;
using Microsoft.Extensions.Options;

namespace SharedKernel.Presentation.OpenApi;

/// <summary>Validates <see cref="SharedKernelOpenApiOptions"/> at startup, so a misconfiguration stops the host instead of a request.</summary>
internal sealed class SharedKernelOpenApiOptionsValidator : IValidateOptions<SharedKernelOpenApiOptions>
{
    // RFC 9110 section 5.6.2: a field name is a token.
    private static readonly SearchValues<char> TokenCharacters =
        SearchValues.Create("!#$%&'*+-.^_`|~0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SharedKernelOpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ApiKeyHeaderName is { Length: > 0 } header && header.AsSpan().ContainsAnyExcept(TokenCharacters))
        {
            return ValidateOptionsResult.Fail(
                "ApiKeyHeaderName must be an HTTP header name (letters, digits and !#$%&'*+-.^_`|~), or empty to declare no API key scheme.");
        }

        return ValidateOptionsResult.Success;
    }
}
