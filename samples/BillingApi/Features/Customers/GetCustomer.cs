using BillingApi.Domain;
using BillingApi.Infrastructure;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Customers;

/// <summary>A customer with its version, sent as the ETag.</summary>
public sealed record VersionedCustomer(CustomerView Customer, EntityVersion Version);

[RequirePermission(Permissions.Read)]
public sealed record GetCustomer(CustomerId Id) : IQuery<VersionedCustomer>;

/// <remarks>
/// The version is PostgreSQL's <c>xmin</c>, kept by the change tracker — so this read goes through the tracking
/// <see cref="IRepository{TAggregate, TId}"/>. <see cref="IReadRepository{TAggregate, TId}"/> never tracks, and
/// <c>ConcurrencyVersion.Get</c> refuses an untracked entity rather than inventing a version. What reaches the client
/// is an opaque token: the <c>xmin</c> sealed with the customer's identity under the service's key.
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
