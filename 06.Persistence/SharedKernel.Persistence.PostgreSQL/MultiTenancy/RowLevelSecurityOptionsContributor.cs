using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.PostgreSQL.MultiTenancy;

/// <summary>
/// <see cref="IPersistenceOptionsExtension"/> that registers this container's
/// <see cref="RowLevelSecurityConnectionInterceptor"/> singleton on every
/// <see cref="DbContextOptionsBuilder"/>, pooled or not.
/// </summary>
/// <remarks>
/// Mirrors <c>SharedKernel.Persistence.EfCore.Encryption</c>'s
/// <c>EncryptionInterceptorOptionsContributor</c> exactly: constructed once, as a singleton, by
/// <c>EfCorePersistenceBuilderRowLevelSecurityExtensions.WithRowLevelSecurity()</c>, and applies the
/// SAME interceptor instance every time — required because
/// <c>Context.SharedKernelDbContext.OnConfiguring</c> (where <see cref="IPersistenceOptionsExtension"/>
/// contributions normally apply) never runs for a pooled context; that builder's pooled branch applies
/// every registered <see cref="IPersistenceOptionsExtension"/> itself, from the same pool-bound
/// provider its platform interceptors already resolve from, so a Singleton-registered contributor
/// works identically whether pooling is enabled or not.
/// </remarks>
public sealed class RowLevelSecurityOptionsContributor : IPersistenceOptionsExtension
{
    private readonly RowLevelSecurityConnectionInterceptor _interceptor;

    /// <summary>Initialises a new <see cref="RowLevelSecurityOptionsContributor"/>.</summary>
    public RowLevelSecurityOptionsContributor(RowLevelSecurityConnectionInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        _interceptor = interceptor;
    }

    /// <inheritdoc />
    public void Apply(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.AddInterceptors(_interceptor);
}
