using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Persistence.EfCore.Extensions;

/// <summary>
/// No-op <see cref="IUserContext"/> placeholder registered by <see cref="EfCorePersistenceBuilder{TContext}.Build"/>
/// when no real <see cref="IUserContext"/> implementation is present in the DI container.
/// Returns <see cref="Guid.Empty"/> for <see cref="IUserContext.UserId"/> and
/// <see langword="false"/> for <see cref="IUserContext.IsAuthenticated"/>, causing interceptors
/// to write <c>"system"</c> as the audit identifier.
/// </summary>
/// <remarks>
/// Override this by registering a scoped <see cref="IUserContext"/> implementation (e.g., from
/// <c>SharedKernel.Security.Oidc</c>) before or after calling <c>.Build()</c> — the last
/// registration wins in standard .NET DI.
/// </remarks>
internal sealed class NoOpUserContext : IUserContext
{
    /// <inheritdoc />
    public Guid UserId => Guid.Empty;

    /// <inheritdoc />
    public string? Email => null;

    /// <inheritdoc />
    public string? Username => null;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles => [];

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Claims => new Dictionary<string, string>();

    /// <inheritdoc />
    public bool IsAuthenticated => false;

    /// <inheritdoc />
    public bool HasRole(string role) => false;
}
