using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// <see cref="IPersistenceOptionsExtension"/> that registers this container's
/// <see cref="RowLevelSecurityConnectionInterceptor"/> and <see cref="RowLevelSecurityCommandInterceptor"/>
/// singletons on every <see cref="DbContextOptionsBuilder"/>, pooled or not.
/// </summary>
/// <remarks>
/// Mirrors <c>SharedKernel.Persistence.EfCore.Encryption</c>'s
/// <c>EncryptionInterceptorOptionsContributor</c> exactly: constructed once, as a singleton, by
/// <c>EfCorePersistenceBuilderRowLevelSecurityExtensions.WithRowLevelSecurity()</c>, and applies the
/// SAME interceptor instances every time — required because
/// <c>Context.SharedKernelDbContext.OnConfiguring</c> (where <see cref="IPersistenceOptionsExtension"/>
/// contributions normally apply) never runs for a pooled context; that builder's pooled branch applies
/// every registered <see cref="IPersistenceOptionsExtension"/> itself, from the same pool-bound
/// provider its platform interceptors already resolve from, so a Singleton-registered contributor
/// works identically whether pooling is enabled or not.
/// </remarks>
public sealed class RowLevelSecurityOptionsContributor : IPersistenceOptionsExtension
{
    private readonly RowLevelSecurityConnectionInterceptor _connectionInterceptor;
    private readonly RowLevelSecurityCommandInterceptor _commandInterceptor;

    /// <summary>Initialises a new <see cref="RowLevelSecurityOptionsContributor"/>.</summary>
    public RowLevelSecurityOptionsContributor(
        RowLevelSecurityConnectionInterceptor connectionInterceptor,
        RowLevelSecurityCommandInterceptor commandInterceptor)
    {
        ArgumentNullException.ThrowIfNull(connectionInterceptor);
        ArgumentNullException.ThrowIfNull(commandInterceptor);
        _connectionInterceptor = connectionInterceptor;
        _commandInterceptor = commandInterceptor;
    }

    /// <inheritdoc />
    public void Apply(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(_connectionInterceptor, _commandInterceptor);
}
