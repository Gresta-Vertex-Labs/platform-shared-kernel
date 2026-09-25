using BillingApi.Domain;
using SharedKernel.Application;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Customers;

/// <summary>A soft delete (the row stays, filtered from every query), only of the version the client last read.</summary>
[RequirePermission(Permissions.Write)]
public sealed record DeleteCustomer(CustomerId Id, EntityVersion ExpectedVersion) : ICommand, IAuditableRequest<Result>
{
    public string Action => "customer.deleted";
    public string ResourceType => nameof(Customer);
    public string ResourceId => Id.Value.ToString();
    public string? BeforeSnapshot => null;
    public string? GetAfterSnapshot(Result response) => null;
}

public sealed class DeleteCustomerHandler(IRepository<Customer, CustomerId> customers) : ICommandHandler<DeleteCustomer>
{
    public async Task<Result> Handle(DeleteCustomer command, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(command.Id, cancellationToken);
        if (customer is null)
            return Result.Failure(CustomerErrors.NotFound(command.Id));

        // Like the rename: a stale version fails the save with ConflictException (persistence.concurrency_conflict).
        await customers.DeleteAsync(customer, command.ExpectedVersion, cancellationToken);
        return Result.Success();
    }
}
