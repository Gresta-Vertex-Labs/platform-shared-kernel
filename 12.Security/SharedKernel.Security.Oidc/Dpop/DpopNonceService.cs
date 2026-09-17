using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Oidc.Options;

namespace SharedKernel.Security.Oidc.Dpop;

// Stateless server nonces (RFC 9449 section 8): the expiry time and random bytes, protected with Data Protection so
// any replica sharing the key ring can check them.
internal sealed class DpopNonceService(
    IDataProtectionProvider dataProtection,
    IClock clock,
    IOptionsMonitor<OidcAuthenticationOptions> options)
{
    private const int PayloadLength = sizeof(long) + 16;

    private readonly IDataProtector _protector = dataProtection.CreateProtector("SharedKernel.Security.Oidc.DpopNonce.v1");

    public string Create()
    {
        Span<byte> payload = stackalloc byte[PayloadLength];
        DateTimeOffset expiresAt = clock.UtcNow + options.CurrentValue.Dpop.NonceLifetime;
        BinaryPrimitives.WriteInt64BigEndian(payload, expiresAt.ToUnixTimeSeconds());
        RandomNumberGenerator.Fill(payload[sizeof(long)..]);
        return Base64Url.EncodeToString(_protector.Protect(payload.ToArray()));
    }

    public bool IsValid(string? nonce)
    {
        if (string.IsNullOrEmpty(nonce) || nonce.Length > 512)
        {
            return false;
        }

        try
        {
            byte[] payload = _protector.Unprotect(Base64Url.DecodeFromChars(nonce));
            if (payload.Length != PayloadLength)
            {
                return false;
            }

            long expiresAt = BinaryPrimitives.ReadInt64BigEndian(payload);
            return clock.UtcNow.ToUnixTimeSeconds() <= expiresAt;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
