using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Oidc.Logging;
using SharedKernel.Security.Oidc.Options;

namespace SharedKernel.Security.Oidc.Revocation;

// Runs the registered ITokenRevocationCheck through the optional cache. Fails closed.
internal sealed class TokenRevocationEnforcer(ITokenRevocationCheck check, ITokenRevocationCache? cache = null)
{
    public async Task<bool> IsRevokedAsync(
        JsonWebToken token,
        IUserContext user,
        TokenRevocationOptions options,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        string hash = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(token.EncodedToken)));
        DateTimeOffset expiresAt = new(DateTime.SpecifyKind(token.ValidTo, DateTimeKind.Utc));

        if (cache is not null)
        {
            try
            {
                bool? cached = await cache.GetAsync(hash, cancellationToken).ConfigureAwait(false);
                if (cached is { } answer)
                {
                    if (answer)
                    {
                        OidcLog.TokenRevocationRejected(logger, checkAvailable: true);
                    }

                    return answer;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                OidcLog.RevocationCacheFailed(logger, ex);
            }
        }

        bool revoked;
        try
        {
            var request = new TokenRevocationRequest(
                token.EncodedToken,
                hash,
                token.TryGetPayloadValue(SecurityClaimTypes.TokenId, out string? jti) ? jti : null,
                user.SubjectId,
                user.ClientId,
                user.SessionId,
                expiresAt);
            revoked = await check.IsRevokedAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OidcLog.TokenRevocationRejected(logger, checkAvailable: false);
            return true;
        }

        if (revoked)
        {
            OidcLog.TokenRevocationRejected(logger, checkAvailable: true);
        }

        if (cache is not null)
        {
            DateTimeOffset cacheUntil = revoked ? expiresAt : Min(now + options.NotRevokedCacheDuration, expiresAt);
            if (cacheUntil > now)
            {
                try
                {
                    await cache.SetAsync(hash, revoked, cacheUntil, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    OidcLog.RevocationCacheFailed(logger, ex);
                }
            }
        }

        return revoked;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
}
