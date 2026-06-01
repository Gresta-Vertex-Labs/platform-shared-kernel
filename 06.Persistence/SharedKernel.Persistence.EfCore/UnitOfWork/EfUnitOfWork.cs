using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.Persistence.EfCore.UnitOfWork;

/// <summary>
/// EF Core implementation of the unit-of-work commit boundary.
/// Delegates <see cref="SaveChangesAsync"/> to
/// <see cref="SharedKernelDbContext.SaveChangesAsync(CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the <strong>only</strong> permitted save boundary. Calling
/// <c>DbContext.SaveChangesAsync</c> directly anywhere outside this class is a hard violation
/// of the persistence architecture rules.
/// </para>
/// <para>
/// All three EF Core interceptors (Audit, SoftDelete, Concurrency) fire automatically within
/// this call before the database commit is issued.
/// </para>
/// </remarks>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly SharedKernelDbContext _dbContext;

    /// <summary>
    /// Initialises a new <see cref="EfUnitOfWork"/>.
    /// </summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    public EfUnitOfWork(SharedKernelDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _dbContext.SaveChangesAsync(ct);
}
