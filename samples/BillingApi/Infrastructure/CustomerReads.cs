using BillingApi.Application;
using BillingApi.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Primitives.Results;

namespace BillingApi.Infrastructure;

/// <summary>
/// Equality lookup on an encrypted column: <c>WhereEncryptedEquals</c> hashes the input with the column's blind-index
/// key (after the column's normalization) and filters on the index. Any other LINQ use of <c>Email</c> is refused
/// before the query runs.
/// </summary>
public sealed class CustomerDirectory(BillingDbContext db) : ICustomerDirectory
{
    public Task<Customer?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        db.Customers.WhereEncryptedEquals(c => c.Email, email).SingleOrDefaultAsync(cancellationToken);
}

/// <summary>A customer with its version, sent as the ETag.</summary>
public sealed record VersionedCustomer(CustomerView Customer, EntityVersion Version);

public sealed record GetCustomer(CustomerId Id) : IQuery<VersionedCustomer>, IAuthorizeRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Read];
    public PermissionMatch PermissionMatch => PermissionMatch.All;
}

/// <remarks>
/// The version is PostgreSQL's <c>xmin</c>, kept by the change tracker — so this read goes through the tracking
/// <see cref="IRepository{TAggregate, TId}"/>. <see cref="IReadRepository{TAggregate, TId}"/> never tracks, and
/// <c>ConcurrencyVersion.Get</c> refuses an untracked entity rather than inventing a version.
/// </remarks>
public sealed class GetCustomerHandler(IRepository<Customer, CustomerId> customers, BillingDbContext db)
    : IQueryHandler<GetCustomer, VersionedCustomer>
{
    public async Task<Result<VersionedCustomer>> Handle(GetCustomer query, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(query.Id, cancellationToken);
        if (customer is null)
            return Result<VersionedCustomer>.Failure(CustomerErrors.NotFound(query.Id));

        return Result<VersionedCustomer>.Success(new VersionedCustomer(customer.ToView(), ConcurrencyVersion.Get(db, customer)));
    }
}
