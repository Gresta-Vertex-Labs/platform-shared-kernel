using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Abstractions.Claims;
using SharedKernel.Security.Oidc.Dpop;
using SharedKernel.Security.Oidc.Logging;
using SharedKernel.Security.Oidc.Options;

namespace SharedKernel.Security.Oidc.Mapping;

/// <summary>
/// Maps a <see cref="ClaimsPrincipal"/> to <see cref="IUserContext"/> for OIDC/JWT requests.
/// </summary>
/// <remarks>
/// <para>
/// Constructed per-request from <c>IHttpContextAccessor.HttpContext?.User</c> plus the currently
/// configured <see cref="ClaimMappingOptions"/>. Never cached across requests.
/// </para>
/// <para>
/// <see cref="Email"/>/<see cref="Username"/>/<see cref="Roles"/>/<see cref="Permissions"/> are resolved
/// via the injected <see cref="ClaimMappingOptions"/> claim-type names — not the legacy
/// <see cref="SecurityClaimTypes.Email"/>/<see cref="ClaimTypes.Name"/>/<see cref="SecurityClaimTypes.Role"/>
/// constants (WO-057, P-366). <see cref="UserId"/> is still always resolved from
/// <see cref="SecurityClaimTypes.UserId"/> (<c>"sub"</c>) — already a short name, unaffected.
/// </para>
/// <para>
/// <b>IdentityKind resolution (corrected WO-057, P-367):</b>
/// <list type="bullet">
///   <item><see cref="Security.Abstractions.Abstractions.IdentityKind.User"/> — the principal is
///     authenticated <b>and</b> a parseable, non-empty <see cref="Guid"/> human subject (<c>sub</c>)
///     claim is present.</item>
///   <item><see cref="Security.Abstractions.Abstractions.IdentityKind.ServicePrincipal"/> — the
///     principal is authenticated but carries no such human subject (the standard client-credentials/
///     machine-to-machine token shape). <see cref="IsAuthenticated"/> stays <see langword="true"/>;
///     <see cref="UserId"/> stays <see cref="Guid.Empty"/>. This is <b>not</b> the same branch as a
///     rejected token.</item>
///   <item><see cref="Security.Abstractions.Abstractions.IdentityKind.Anonymous"/> — the underlying
///     <see cref="ClaimsPrincipal.Identity"/> itself is not authenticated. This is the only case that
///     forces <see cref="IsAuthenticated"/> = <see langword="false"/>.</item>
/// </list>
/// Detection is IdP-agnostic — it never keys off a single identity provider's proprietary claim names.
/// </para>
/// <para>
/// AOT note: <see cref="ClaimsPrincipal.Claims"/> iteration does not use reflection on user types. The
/// defensive role-claim reader (JSON-array-valued claim shape) uses <see cref="System.Text.Json"/>,
/// scoped narrowly to that one parse path.
/// </para>
/// </remarks>
public sealed class OidcUserContext : IUserContext
{
    /// <inheritdoc/>
    public Guid UserId { get; }

    /// <inheritdoc/>
    public string? Email { get; }

    /// <inheritdoc/>
    public string? Username { get; }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Roles { get; }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Permissions { get; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Claims { get; }

    /// <inheritdoc/>
    public bool IsAuthenticated { get; }

    /// <inheritdoc/>
    public IdentityKind IdentityKind { get; }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> AuthenticationMethods { get; }

    /// <inheritdoc/>
    public string? AuthContextClassReference { get; }

    /// <inheritdoc/>
    public DateTimeOffset? AuthTime { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Set to <see langword="true"/> only when <c>DpopProofValidator</c> has confirmed a fresh,
    /// correctly-bound DPoP proof for the current request by stamping the internal
    /// <c>Dpop.DpopClaimTypes.SenderConstrained</c> claim onto the validated principal
    /// (<see cref="System.Security.Claims.ClaimsPrincipal"/>) during <c>OnTokenValidated</c>. Always
    /// <see langword="false"/> for an ordinary bearer token, even when DPoP is enabled host-wide
    /// (WO-058, P-376).
    /// </remarks>
    public bool IsSenderConstrained { get; }

    /// <summary>
    /// Initialises a new <see cref="OidcUserContext"/> from the supplied <paramref name="principal"/>.
    /// </summary>
    /// <param name="principal">
    /// The <see cref="ClaimsPrincipal"/> representing the current request's identity.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="claimMapping">
    /// The claim-type mapping to resolve <see cref="Email"/>/<see cref="Username"/>/<see cref="Roles"/>/
    /// <see cref="Permissions"/> from. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="logger">
    /// An optional logger for structured security-audit events (missing/unparseable human subject,
    /// service-principal recognition). No raw claim values are ever logged. <see langword="null"/> is
    /// accepted for direct, non-DI construction (e.g. unit tests).
    /// </param>
    public OidcUserContext(ClaimsPrincipal principal, ClaimMappingOptions claimMapping, ILogger<OidcUserContext>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(claimMapping);

        // Build the Claims dictionary — first value per claim type wins.
        var claimsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in principal.Claims)
        {
            claimsDict.TryAdd(claim.Type, claim.Value);
        }

        Claims = claimsDict;
        Roles = ReadDefensiveMultiValueClaim(principal, claimMapping.RoleClaimType);
        Permissions = ReadSpaceDelimitedClaim(claimsDict, claimMapping.PermissionClaimType);
        AuthenticationMethods = ReadDefensiveAmrClaim(principal, claimMapping.AmrClaimType);

        Email = claimsDict.TryGetValue(claimMapping.EmailClaimType, out var email) ? email : null;
        Username = claimsDict.TryGetValue(claimMapping.NameClaimType, out var name) ? name : null;
        AuthContextClassReference = claimsDict.TryGetValue(claimMapping.AcrClaimType, out var acr) ? acr : null;
        AuthTime = ParseAuthTime(claimsDict, claimMapping.AuthTimeClaimType);
        IsSenderConstrained = claimsDict.TryGetValue(DpopClaimTypes.SenderConstrained, out var dpopBound)
            && string.Equals(dpopBound, bool.TrueString, StringComparison.OrdinalIgnoreCase);

        var baseAuthenticated = principal.Identity?.IsAuthenticated ?? false;
        var subjectClaimPresent = claimsDict.TryGetValue(SecurityClaimTypes.UserId, out var sub);
        var parsedUserId = Guid.Empty;
        var hasHumanSubject = subjectClaimPresent
            && Guid.TryParse(sub, out parsedUserId)
            && parsedUserId != Guid.Empty;

        if (baseAuthenticated && hasHumanSubject)
        {
            UserId = parsedUserId;
            IsAuthenticated = true;
            IdentityKind = IdentityKind.User;
        }
        else if (baseAuthenticated)
        {
            // Authenticated, but no parseable human subject — the standard client-credentials/M2M shape.
            // This is NOT a rejected token; IsAuthenticated stays true (corrected invariant, WO-057/P-367).
            UserId = Guid.Empty;
            IsAuthenticated = true;
            IdentityKind = IdentityKind.ServicePrincipal;

            if (logger is not null)
            {
                SecurityLogEvents.ServicePrincipalRecognized(logger, subjectClaimPresent);
            }
        }
        else
        {
            UserId = Guid.Empty;
            IsAuthenticated = false;
            IdentityKind = IdentityKind.Anonymous;
        }
    }

    /// <inheritdoc/>
    public bool HasRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool WasAuthenticatedWith(string method) =>
        AuthenticationMethods.Contains(method, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) =>
        AuthTime.HasValue && (now - AuthTime.Value) <= maxAge;

    /// <summary>
    /// Reads every claim of type <paramref name="claimType"/> from <paramref name="principal"/>,
    /// defensively handling both real-world shapes: one <see cref="Claim"/> per role, or a single claim
    /// whose value is a JSON array of roles. Never throws on either shape.
    /// </summary>
    private static IReadOnlyCollection<string> ReadDefensiveMultiValueClaim(ClaimsPrincipal principal, string claimType)
    {
        List<string>? rawValues = null;
        foreach (var claim in principal.Claims)
        {
            if (string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            {
                (rawValues ??= []).Add(claim.Value);
            }
        }

        if (rawValues is null || rawValues.Count == 0)
        {
            return [];
        }

        // Exactly one claim whose value looks like a JSON array — the "single claim, JSON-array value" shape.
        if (rawValues.Count == 1 && TryParseJsonStringArray(rawValues[0], out var arrayValues))
        {
            return arrayValues;
        }

        // Otherwise — one Claim per role, the common shape.
        return rawValues;
    }

    private static bool TryParseJsonStringArray(string value, out List<string> values)
    {
        values = [];

        var trimmed = value.AsSpan().Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']')
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String && element.GetString() is { } s)
                {
                    values.Add(s);
                }
            }

            return true;
        }
        catch (JsonException)
        {
            // Not valid JSON — treat the raw value as a single, non-array role entry instead.
            values = [];
            return false;
        }
    }

    private static IReadOnlyCollection<string> ReadSpaceDelimitedClaim(
        IReadOnlyDictionary<string, string> claims,
        string claimType)
    {
        if (!claims.TryGetValue(claimType, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Reads every claim of type <paramref name="claimType"/> (the OIDC <c>amr</c> claim), defensively
    /// handling both real-world shapes: one <see cref="Claim"/> per authentication method, or a single
    /// claim whose value is a space-delimited list of methods. Never throws on either shape (WO-058, P-375).
    /// </summary>
    private static IReadOnlyCollection<string> ReadDefensiveAmrClaim(ClaimsPrincipal principal, string claimType)
    {
        List<string>? rawValues = null;
        foreach (var claim in principal.Claims)
        {
            if (string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            {
                (rawValues ??= []).Add(claim.Value);
            }
        }

        if (rawValues is null || rawValues.Count == 0)
        {
            return [];
        }

        // Exactly one claim whose value contains spaces — the "single claim, space-delimited" shape.
        if (rawValues.Count == 1 && rawValues[0].Contains(' '))
        {
            return rawValues[0].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        // Otherwise — one Claim per method, the common shape.
        return rawValues;
    }

    /// <summary>
    /// Parses the OIDC <c>auth_time</c> claim (a NumericDate — Unix seconds) into a
    /// <see cref="DateTimeOffset"/>. Absent or unparseable values yield <see langword="null"/>, never a
    /// throw (WO-058, P-375).
    /// </summary>
    private static DateTimeOffset? ParseAuthTime(IReadOnlyDictionary<string, string> claims, string claimType)
    {
        if (!claims.TryGetValue(claimType, out var raw) || !long.TryParse(raw, out var seconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
