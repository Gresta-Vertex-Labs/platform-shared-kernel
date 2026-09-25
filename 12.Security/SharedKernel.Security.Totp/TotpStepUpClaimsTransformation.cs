using SharedKernel.Execution.Context;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Security.Totp;

/// <summary>
/// Adds the <c>otp</c> authentication method to the caller's identity while its session has a recent step-up.
/// </summary>
/// <remarks>
/// <para>
/// Runs after authentication and before <see cref="IUserContext"/> is resolved, so
/// <see cref="IUserContext.WasAuthenticatedWith"/> sees the method. It never changes <see cref="IUserContext.AuthTime"/>.
/// </para>
/// <para>
/// ASP.NET Core resolves a single <see cref="IClaimsTransformation"/>. <c>AddTotpStepUp</c> wraps one registered
/// earlier, which runs first; register any other transformation before <c>AddTotpStepUp</c>.
/// </para>
/// </remarks>
public sealed class TotpStepUpClaimsTransformation : IClaimsTransformation
{
    private readonly IEnumerable<IUserContextMapper> _mappers;
    private readonly ITotpStepUpStore _store;
    private readonly IClock _clock;
    private readonly IOptions<TotpStepUpOptions> _options;
    private readonly IClaimsTransformation? _inner;

    /// <summary>Creates the transformation.</summary>
    /// <param name="mappers">The registered mappers, used to find the caller's subject and session.</param>
    /// <param name="store">Holds step-ups.</param>
    /// <param name="clock">The time source.</param>
    /// <param name="options">The step-up options.</param>
    /// <param name="inner">A transformation to run first, or <see langword="null"/>.</param>
    public TotpStepUpClaimsTransformation(
        IEnumerable<IUserContextMapper> mappers,
        ITotpStepUpStore store,
        IClock clock,
        IOptions<TotpStepUpOptions> options,
        IClaimsTransformation? inner = null)
    {
        ArgumentNullException.ThrowIfNull(mappers);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        _mappers = mappers;
        _store = store;
        _clock = clock;
        _options = options;
        _inner = inner;
    }

    /// <inheritdoc/>
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (_inner is not null)
        {
            principal = await _inner.TransformAsync(principal).ConfigureAwait(false);
        }

        IUserContextMapper[] mappers = [.. _mappers];
        ClaimsIdentity? identity = principal.Identities.FirstOrDefault(candidate =>
            candidate.IsAuthenticated
            && mappers.Any(mapper => string.Equals(mapper.AuthenticationType, candidate.AuthenticationType, StringComparison.Ordinal)));
        if (identity is null)
        {
            return principal;
        }

        IUserContext user = UserContextResolver.Resolve(new ClaimsPrincipal(identity), mappers);
        if (user.ActorKind != ActorKind.User || user.SubjectId is not { } subjectId || user.SessionId is not { } sessionId)
        {
            return principal;
        }

        TotpStepUpOptions options = _options.Value;
        if (identity.HasClaim(options.AuthenticationMethodClaimType, options.AuthenticationMethod))
        {
            return principal;
        }

        DateTimeOffset? verifiedAt = await _store.GetLastVerifiedAsync(subjectId, sessionId, CancellationToken.None).ConfigureAwait(false);
        DateTimeOffset now = _clock.UtcNow;
        if (verifiedAt is not { } at || at > now || now - at > options.FreshnessWindow)
        {
            return principal;
        }

        var steppedUp = new ClaimsIdentity(identity, [new Claim(options.AuthenticationMethodClaimType, options.AuthenticationMethod)]);
        return new ClaimsPrincipal(principal.Identities.Select(candidate => ReferenceEquals(candidate, identity) ? steppedUp : candidate));
    }
}
