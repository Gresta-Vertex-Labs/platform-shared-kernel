using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// <see cref="IPersistenceOptionsExtension"/> that registers this container's
/// <see cref="AuditRecordMutationGuardInterceptor"/> singleton on every <see cref="DbContextOptionsBuilder"/>,
/// pooled or not.
/// </summary>
/// <remarks>
/// Deliberately NOT wired through <c>EfCorePersistenceBuilder.AddInterceptor&lt;T&gt;()</c>, which only
/// accepts an <see cref="Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor"/> —
/// <see cref="AuditRecordMutationGuardInterceptor"/> is a
/// <see cref="Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor"/> instead, the same
/// shape as <c>SharedKernel.Persistence.EfCore.Encryption</c>'s
/// <c>EncryptedColumnEqualityGuardInterceptor</c>/<c>EncryptionInterceptorOptionsContributor</c> pair,
/// which this class mirrors exactly.
/// </remarks>
public sealed class AuditRecordMutationGuardOptionsContributor : IPersistenceOptionsExtension
{
    private readonly AuditRecordMutationGuardInterceptor _interceptor = new();

    /// <inheritdoc />
    public void Apply(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(_interceptor);
}
