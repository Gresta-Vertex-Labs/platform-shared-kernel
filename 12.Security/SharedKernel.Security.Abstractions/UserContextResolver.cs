using System.Security.Claims;

namespace SharedKernel.Security.Abstractions;

/// <summary>Resolves the <see cref="IUserContext"/> of a principal from the registered mappers.</summary>
public static class UserContextResolver
{
    /// <summary>
    /// Maps the first authenticated identity of <paramref name="principal"/> that has a matching mapper.
    /// </summary>
    /// <param name="principal">The principal, or <see langword="null"/> outside a request.</param>
    /// <param name="mappers">The registered mappers.</param>
    /// <returns>
    /// The mapped context, or <see cref="AnonymousUserContext.Instance"/> when the principal is missing, not
    /// authenticated, or authenticated only by schemes no mapper handles. An unknown scheme never becomes an
    /// authenticated context.
    /// </returns>
    public static IUserContext Resolve(ClaimsPrincipal? principal, IEnumerable<IUserContextMapper> mappers)
    {
        ArgumentNullException.ThrowIfNull(mappers);

        if (principal is null)
        {
            return AnonymousUserContext.Instance;
        }

        IUserContextMapper[] candidates = [.. mappers];
        foreach (ClaimsIdentity identity in principal.Identities)
        {
            if (!identity.IsAuthenticated)
            {
                continue;
            }

            foreach (IUserContextMapper mapper in candidates)
            {
                if (string.Equals(mapper.AuthenticationType, identity.AuthenticationType, StringComparison.Ordinal))
                {
                    return mapper.Map(identity);
                }
            }
        }

        return AnonymousUserContext.Instance;
    }
}
