namespace SharedKernel.Security.Abstractions;

/// <summary>The kind of caller an <see cref="IUserContext"/> represents.</summary>
public enum IdentityKind
{
    /// <summary>
    /// No authenticated caller. This is <c>default(IdentityKind)</c>, so an uninitialized value fails closed.
    /// </summary>
    Anonymous = 0,

    /// <summary>A person, authenticated by an identity provider.</summary>
    User,

    /// <summary>
    /// An application acting for itself: an OAuth client-credentials token, an API key or a client certificate.
    /// </summary>
    ServicePrincipal,

    /// <summary>
    /// Trusted code running without a caller, such as a scheduled job or a message consumer. See
    /// <see cref="SystemUserContext"/>.
    /// </summary>
    System,
}
