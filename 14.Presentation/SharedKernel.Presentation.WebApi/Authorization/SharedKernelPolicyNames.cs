using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Encodes the requirement of an authorization attribute into the policy name it hands ASP.NET Core, and decodes
/// it back in the policy provider, so no policy has to be registered up front.
/// </summary>
/// <remarks>
/// A name is <c>SharedKernel:{kind}:{value}|{value}…</c>. Values are split on <c>|</c> only, so a permission such as
/// <c>orders:read</c> survives; the attributes refuse values containing <c>|</c>. An authentication-method requirement
/// with a maximum age has its own kind and puts the age first, <c>SharedKernel:amr-max-age:{seconds}|{method}|…</c>, so a
/// method that looks like a number is never read as the age.
/// </remarks>
internal static class SharedKernelPolicyNames
{
    public const string Prefix = "SharedKernel:";

    public const char ValueSeparator = '|';

    private const char KindSeparator = ':';

    private const string PermissionKind = "permission";

    private const string RoleKind = "role";

    private const string FreshAuthenticationKind = "fresh";

    private const string AuthenticationMethodKind = "amr";

    private const string RecentAuthenticationMethodKind = "amr-max-age";

    public static string ForPermissions(IEnumerable<string> permissions) => Encode(PermissionKind, permissions);

    public static string ForRoles(IEnumerable<string> roles) => Encode(RoleKind, roles);

    public static string ForFreshAuthentication(int maxAgeSeconds) =>
        Encode(FreshAuthenticationKind, [maxAgeSeconds.ToString(CultureInfo.InvariantCulture)]);

    public static string ForAuthenticationMethods(IEnumerable<string> methods) => Encode(AuthenticationMethodKind, methods);

    public static string ForAuthenticationMethods(IEnumerable<string> methods, int maxAgeSeconds) =>
        Encode(RecentAuthenticationMethodKind, [maxAgeSeconds.ToString(CultureInfo.InvariantCulture), .. methods]);

    public static bool IsSharedKernelPolicy(string? policyName) =>
        policyName is not null && policyName.StartsWith(Prefix, StringComparison.Ordinal);

    public static bool TryCreateRequirement(string policyName, [NotNullWhen(true)] out SharedKernelRequirement? requirement)
    {
        requirement = null;

        var body = policyName.AsSpan(Prefix.Length);
        var kindEnd = body.IndexOf(KindSeparator);
        if (kindEnd <= 0 || kindEnd == body.Length - 1)
        {
            return false;
        }

        var kind = body[..kindEnd].ToString();
        var values = body[(kindEnd + 1)..].ToString().Split(ValueSeparator);
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        requirement = kind switch
        {
            PermissionKind => new PermissionRequirement(values),
            RoleKind => new RoleRequirement(values),
            AuthenticationMethodKind => new AuthenticationMethodRequirement(values),
            RecentAuthenticationMethodKind when values.Length > 1 && TryParseMaxAge(values[0], out var methodMaxAge) =>
                new AuthenticationMethodRequirement(values[1..], methodMaxAge),
            FreshAuthenticationKind when values.Length == 1 && TryParseMaxAge(values[0], out var maxAge) =>
                new FreshAuthenticationRequirement(maxAge),
            _ => null,
        };

        return requirement is not null;
    }

    /// <summary>Checks the values of an attribute and returns a copy.</summary>
    public static string[] ValidateValues(string[] values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);

        if (values.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", parameterName);
        }

        var copy = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Values must not be null, empty or white space.", parameterName);
            }

            if (value.Contains(ValueSeparator, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Values must not contain '{ValueSeparator}'.", parameterName);
            }

            copy[i] = value;
        }

        return copy;
    }

    private static string Encode(string kind, IEnumerable<string> values) =>
        Prefix + kind + KindSeparator + string.Join(ValueSeparator, values);

    // A whole number of seconds, greater than zero, written as the attributes write it: digits only.
    private static bool TryParseMaxAge(string value, out TimeSpan maxAge)
    {
        var parsed = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds > 0;
        maxAge = parsed ? TimeSpan.FromSeconds(seconds) : default;
        return parsed;
    }
}
