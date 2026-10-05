namespace SharedKernel.Security.Oidc.Options;

/// <summary>Settings for DPoP sender-constrained access tokens (RFC 9449).</summary>
public sealed class DpopOptions
{
    /// <summary>
    /// Gets or sets whether plain bearer tokens are still accepted. Defaults to <see cref="DpopMode.Allowed"/>:
    /// DPoP-bound tokens need a valid proof, and unbound bearer tokens keep working.
    /// </summary>
    public DpopMode Mode { get; set; } = DpopMode.Allowed;

    /// <summary>
    /// Gets or sets the signing algorithms accepted for proofs. Empty, the default, accepts <c>ES256</c>,
    /// <c>PS256</c> and <c>RS256</c>; only asymmetric algorithms are allowed. A configured list replaces the defaults.
    /// </summary>
    public List<string> ValidAlgorithms { get; set; } = [];

    /// <summary>
    /// Gets or sets how long after its <c>iat</c> a proof is accepted, from one second to five minutes. Defaults to
    /// 60 seconds. The replay cache keeps each proof this long plus <see cref="ClockSkew"/>.
    /// </summary>
    public TimeSpan ProofLifetime { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the tolerated clock difference with clients, from zero to one minute. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets a value indicating whether proofs must carry a nonce issued by this service. Defaults to
    /// <see langword="false"/>. A nonce limits proofs a client created in advance to <see cref="NonceLifetime"/>.
    /// </summary>
    /// <remarks>
    /// Nonces are protected with ASP.NET Core Data Protection. Every replica needs the same key ring, stored
    /// outside the container.
    /// </remarks>
    public bool RequireNonce { get; set; }

    /// <summary>Gets or sets how long an issued nonce is accepted, from one minute to one hour. Defaults to 5 minutes.</summary>
    public TimeSpan NonceLifetime { get; set; } = TimeSpan.FromMinutes(5);
}
