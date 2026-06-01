using SharedKernel.Persistence.EfCore.Interceptors;

namespace SharedKernel.Persistence.EfCore.Extensions;

/// <summary>
/// No-op <see cref="IUserContext"/> placeholder registered by <see cref="EfCorePersistenceBuilder{TContext}.Build"/>
/// when no real <see cref="IUserContext"/> implementation is present in the DI container.
/// Always returns the literal string <c>"system"</c>.
/// </summary>
/// <remarks>
/// Override this by registering a scoped <see cref="IUserContext"/> implementation (e.g., from
/// <c>SharedKernel.Security.Oidc</c>) before or after calling <c>.Build()</c> — the last
/// registration wins in standard .NET DI.
/// </remarks>
internal sealed class NoOpUserContext : IUserContext
{
    /// <inheritdoc />
    public string UserId => "system";
}
