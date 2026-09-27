using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Communication;

/// <summary>
/// What every outbound client has, whatever its protocol: how it authenticates and its TLS settings. The REST and gRPC
/// client options derive from it and are bound from <c>SharedKernel:Communication:Clients:{name}</c>.
/// </summary>
public abstract class CommunicationClientOptions : IValidatableObject
{
    /// <summary>Gets or sets how the client authenticates to the service it calls. None by default.</summary>
    public ClientAuthenticationOptions Authentication { get; set; } = new();

    /// <summary>Gets or sets the client certificate and the certificate authorities the server is trusted by.</summary>
    public ClientTlsOptions Tls { get; set; } = new();

    /// <inheritdoc />
    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in Authentication.Validate())
        {
            yield return result;
        }

        foreach (var result in Tls.Validate())
        {
            yield return result;
        }
    }

    /// <summary>The failure of an address that is not absolute, or whose scheme a client cannot call.</summary>
    /// <param name="address">The address to check, or <see langword="null"/>.</param>
    /// <param name="memberName">The member that holds it.</param>
    /// <returns>A failure, or <see langword="null"/> when the address is usable.</returns>
    private protected static ValidationResult? ValidateAddress(Uri? address, string memberName)
    {
        if (address is null)
        {
            return new ValidationResult(
                $"{memberName} is required: the address of the service, such as http://inventory or https+http://_grpc.inventory.",
                [memberName]);
        }

        if (!address.IsAbsoluteUri || address.Scheme is not ("http" or "https" or "https+http" or "http+https"))
        {
            return new ValidationResult(
                $"{memberName} '{address}' must be an absolute http, https, https+http or http+https address.",
                [memberName]);
        }

        return null;
    }
}
