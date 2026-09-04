using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using SharedKernel.Security.Abstractions.Claims;
using SharedKernel.Security.Totp.Challenge;
using SharedKernel.Security.Totp.Logging;

namespace SharedKernel.Security.Totp.StepUp;

/// <summary>
/// Stamps the configured AMR (Authentication Method Reference) claim onto the current
/// <see cref="ClaimsPrincipal"/> when a fresh, successful TOTP challenge was recorded for it — the
/// ASP.NET Core-facing mechanism that makes a TOTP step-up observable through
/// <c>IUserContext.WasAuthenticatedWith</c>/<c>AuthenticationMethods</c> with zero changes required
/// in <c>SharedKernel.Security.Oidc</c> or <c>14.Presentation</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The composition mechanism:</b> ASP.NET Core invokes every registered
/// <see cref="IClaimsTransformation"/> during <c>AuthenticateAsync</c>, BEFORE any
/// <c>IUserContext</c> DI factory first resolves for the request. So
/// <c>SharedKernel.Security.Oidc</c>'s EXISTING defensive <c>amr</c>-claim reader
/// (<c>OidcUserContext</c>, shipped WO-058/P-375) picks up the claim stamped here with ZERO code
/// change in <c>.Oidc</c> itself — the SAME sanctioned "stamp a synthetic/augmenting marker claim
/// during/after authentication, read it generically downstream" mechanism this domain already
/// established for DPoP's <c>IsSenderConstrained</c> (WO-058), applied via a framework-provided,
/// scheme-agnostic hook instead of a <c>JwtBearerEvents.OnTokenValidated</c> hook (which only
/// <c>.Oidc</c> itself may register, since only <c>.Oidc</c> owns <c>JwtBearerOptions</c>) (WO-069,
/// P-452).
/// </para>
/// <para>
/// <b>Scope boundary:</b> this composes ONLY with <c>.Oidc</c>'s <c>OidcUserContext</c> — the human
/// end-user path, the only implementer that reads <c>amr</c> claims. It has no effect on
/// <c>.ApiKey</c>/<c>.Mtls</c> primary identities: those never carry a parseable <c>sub</c> claim
/// (always <c>UserId == Guid.Empty</c>), which the guard below skips over anyway.
/// </para>
/// <para>
/// <b>Never modifies <c>AuthTime</c>/<c>IsAuthenticationFresherThan</c>.</b> A TOTP step-up only
/// augments <c>AuthenticationMethods</c>/<c>WasAuthenticatedWith</c> — <c>[RequireFreshAuthentication]</c>
/// stays keyed solely on the primary authentication's own recency; <c>[RequireAuthenticationMethod("otp")]</c>
/// is the correct, independent gate for "was TOTP verified," and its own freshness is enforced
/// entirely by <see cref="TotpStepUpOptions.ChallengeFreshnessWindow"/> here.
/// </para>
/// </remarks>
public sealed class TotpStepUpClaimsTransformation : IClaimsTransformation
{
    private readonly ITotpChallengeStore _challengeStore;
    private readonly TotpStepUpOptions _options;
    private readonly ILogger<TotpStepUpClaimsTransformation> _logger;

    /// <summary>Creates a new <see cref="TotpStepUpClaimsTransformation"/>.</summary>
    /// <param name="challengeStore">The consumer-supplied challenge-freshness store.</param>
    /// <param name="options">The AMR claim type/value and freshness window to apply.</param>
    /// <param name="logger">The structured security-audit logger.</param>
    public TotpStepUpClaimsTransformation(ITotpChallengeStore challengeStore, TotpStepUpOptions options, ILogger<TotpStepUpClaimsTransformation> logger)
    {
        ArgumentNullException.ThrowIfNull(challengeStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _challengeStore = challengeStore;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Guards <c>principal.Identity?.IsAuthenticated == true</c> AND a parseable, non-
    /// <see cref="Guid.Empty"/> <c>sub</c> claim BEFORE any store lookup — never looks up an anonymous
    /// or <see cref="Guid.Empty"/> principal. Idempotent: skips if the configured AMR claim/value is
    /// already present, since ASP.NET Core may invoke a registered <see cref="IClaimsTransformation"/>
    /// more than once per request. On a fresh hit, adds exactly one new claim to a NEW
    /// <see cref="ClaimsIdentity"/> — never mutates the original identity instance in place.
    /// </remarks>
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        string? subjectClaim = principal.FindFirst(SecurityClaimTypes.UserId)?.Value;
        if (string.IsNullOrEmpty(subjectClaim) || !Guid.TryParse(subjectClaim, out Guid userId) || userId == Guid.Empty)
        {
            return principal;
        }

        bool alreadyStampedThisValue = principal.Claims.Any(claim =>
            string.Equals(claim.Type, _options.AmrClaimType, StringComparison.Ordinal)
            && string.Equals(claim.Value, _options.AmrValue, StringComparison.Ordinal));
        if (alreadyStampedThisValue)
        {
            return principal;
        }

        string identityKey = TotpIdentityKeyFormatter.Format(userId);
        DateTimeOffset? lastChallenge = await _challengeStore.TryGetLastSuccessfulChallengeAsync(identityKey).ConfigureAwait(false);

        if (lastChallenge is null || DateTimeOffset.UtcNow - lastChallenge.Value > _options.ChallengeFreshnessWindow)
        {
            return principal;
        }

        var newIdentity = new ClaimsIdentity(principal.Identity, [new Claim(_options.AmrClaimType, _options.AmrValue)]);
        var newPrincipal = new ClaimsPrincipal(newIdentity);

        // Preserve every OTHER identity already on the principal (e.g. a second authentication
        // scheme's identity) — ClaimsPrincipal(ClaimsIdentity) above only carries the new primary
        // identity forward; add the rest back untouched.
        foreach (var identity in principal.Identities)
        {
            if (!ReferenceEquals(identity, principal.Identity))
            {
                newPrincipal.AddIdentity(identity);
            }
        }

        SecurityLogEvents.TotpStepUpClaimApplied(_logger);
        return newPrincipal;
    }
}
