using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;

namespace SharedKernel.Security.Abstractions;

/// <summary>
/// Writes and reads <see cref="SecurityClaimTypes.AuthenticationMethodTime"/> claims, which record when an
/// authentication method was verified.
/// </summary>
/// <remarks>
/// <para>
/// A value is <c>{method} {seconds}</c>: the <c>amr</c> value, one space, and the whole seconds since the Unix epoch in
/// invariant digits, such as <c>otp 1790000000</c>. The time is the part after the last space, so a method that
/// contains a space reads back unchanged.
/// </para>
/// <para>
/// Code that adds an authentication method to an identity after sign-in (a step-up claims transformation) adds this
/// claim with it, dated when the method was verified. A method the credential itself carries needs none: it was
/// verified at sign-in, <see cref="IUserContext.AuthTime"/>. Mappers pass <see cref="Read"/> to
/// <see cref="UserContext.AuthenticationMethodTimes"/>, and <see cref="IUserContext.GetAuthenticationMethodTime"/>
/// answers from it.
/// </para>
/// </remarks>
public static class AuthenticationMethodTimeClaim
{
    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    /// <summary>Creates the claim recording that <paramref name="method"/> was verified at <paramref name="verifiedAt"/>.</summary>
    /// <param name="method">The authentication method reference, such as <c>otp</c>.</param>
    /// <param name="verifiedAt">
    /// When the method was verified. Recorded in whole seconds, rounded down, so the claim never makes a method look
    /// more recent than it is.
    /// </param>
    /// <returns>A claim of type <see cref="SecurityClaimTypes.AuthenticationMethodTime"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="method"/> is null, empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="verifiedAt"/> is before the Unix epoch.</exception>
    public static Claim Create(string method, DateTimeOffset verifiedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentOutOfRangeException.ThrowIfLessThan(verifiedAt, DateTimeOffset.UnixEpoch);

        return new Claim(
            SecurityClaimTypes.AuthenticationMethodTime,
            string.Create(CultureInfo.InvariantCulture, $"{method} {verifiedAt.ToUnixTimeSeconds()}"));
    }

    /// <summary>Reads when each authentication method was last verified.</summary>
    /// <param name="claims">The claims of an authenticated identity.</param>
    /// <returns>
    /// The latest time recorded for each method, keyed by method (compared ordinally). Claims of other types and
    /// malformed values are ignored; empty when there are none.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="claims"/> is <see langword="null"/>.</exception>
    public static IReadOnlyDictionary<string, DateTimeOffset> Read(IEnumerable<Claim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        Dictionary<string, DateTimeOffset>? times = null;
        foreach (Claim claim in claims)
        {
            if (!string.Equals(claim.Type, SecurityClaimTypes.AuthenticationMethodTime, StringComparison.Ordinal)
                || !TryParse(claim.Value, out string? method, out DateTimeOffset verifiedAt))
            {
                continue;
            }

            times ??= new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            if (!times.TryGetValue(method, out DateTimeOffset recorded) || verifiedAt > recorded)
            {
                times[method] = verifiedAt;
            }
        }

        return times is null ? ReadOnlyDictionary<string, DateTimeOffset>.Empty : times;
    }

    private static bool TryParse(string value, [NotNullWhen(true)] out string? method, out DateTimeOffset verifiedAt)
    {
        method = null;
        verifiedAt = default;

        int separator = value.LastIndexOf(' ');
        if (separator <= 0)
        {
            return false;
        }

        string candidate = value[..separator];
        if (string.IsNullOrWhiteSpace(candidate)
            || !long.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
            || seconds > MaxUnixSeconds)
        {
            return false;
        }

        method = candidate;
        verifiedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }
}
