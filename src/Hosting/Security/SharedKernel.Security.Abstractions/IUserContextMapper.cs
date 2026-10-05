using System.Security.Claims;

namespace SharedKernel.Security.Abstractions;

/// <summary>
/// Turns the identity produced by one authentication scheme into an <see cref="IUserContext"/>.
/// </summary>
/// <remarks>
/// Each authentication package registers one mapper with <c>TryAddEnumerable</c>. <see cref="UserContextResolver"/>
/// selects the mapper whose <see cref="AuthenticationType"/> equals the identity's
/// <see cref="ClaimsIdentity.AuthenticationType"/>, so registration order never matters.
/// </remarks>
public interface IUserContextMapper
{
    /// <summary>Gets the <see cref="ClaimsIdentity.AuthenticationType"/> this mapper handles, compared ordinally.</summary>
    string AuthenticationType { get; }

    /// <summary>Builds the context for an authenticated identity of <see cref="AuthenticationType"/>.</summary>
    /// <param name="identity">The authenticated identity.</param>
    /// <returns>
    /// The context, or <see cref="AnonymousUserContext.Instance"/> when the identity lacks what the mapper needs
    /// (for example a subject); never <see langword="null"/>.
    /// </returns>
    IUserContext Map(ClaimsIdentity identity);
}
