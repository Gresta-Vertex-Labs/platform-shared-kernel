namespace SharedKernel.Security.Abstractions.Abstractions;

/// <summary>
/// Sentinel implementation of <see cref="IUserContext"/> for unauthenticated requests.
/// </summary>
/// <remarks>
/// <para>
/// Registered by the OIDC package as the DI fallback so that <see cref="IUserContext"/> is
/// always resolvable regardless of authentication state. The scoped factory returns this
/// instance when no <c>HttpContext</c> is present.
/// </para>
/// <para>
/// All string properties are <see langword="null"/>; <see cref="Roles"/> and <see cref="Claims"/>
/// are empty read-only collections; <see cref="UserId"/> is <see cref="Guid.Empty"/>;
/// <see cref="IsAuthenticated"/> is <see langword="false"/>; <see cref="HasRole"/> always
/// returns <see langword="false"/>.
/// </para>
/// </remarks>
public sealed class AnonymousUserContext : IUserContext
{
    /// <summary>Gets the singleton instance of <see cref="AnonymousUserContext"/>.</summary>
    public static readonly AnonymousUserContext Instance = new();

    /// <inheritdoc/>
    public Guid UserId => Guid.Empty;

    /// <inheritdoc/>
    public string? Email => null;

    /// <inheritdoc/>
    public string? Username => null;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Roles => [];

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Claims => EmptyDictionary.Instance;

    /// <inheritdoc/>
    public bool IsAuthenticated => false;

    /// <inheritdoc/>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool HasRole(string role) => false;

    // Immutable empty dictionary singleton — avoids allocation on every property access.
    private static class EmptyDictionary
    {
        internal static readonly IReadOnlyDictionary<string, string> Instance =
            new Dictionary<string, string>(0);
    }
}
