using BillingApi.Domain;
using SharedKernel.Application;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Customers;

/// <summary>Optimistic concurrency: the rename saves only over the version the client read (its <c>If-Match</c>).</summary>
[RequirePermission(Permissions.Write)]
public sealed record RenameCustomer(CustomerId Id, string Name, EntityVersion ExpectedVersion)
    : ICommand, IAuditableRequest<Result>
{
    public string Action => "customer.renamed";
    public string ResourceType => nameof(Customer);
    public string ResourceId => Id.Value.ToString();
    public string? BeforeSnapshot => null;
    public string? GetAfterSnapshot(Result response) => response.IsSuccess ? $$"""{"name":"{{Name}}"}""" : null;
}

public sealed class RenameCustomerHandler(IRepository<Customer, CustomerId> customers) : ICommandHandler<RenameCustomer>
{
    public async Task<Result> Handle(RenameCustomer command, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(command.Id, cancellationToken);
        if (customer is null)
            return Result.Failure(CustomerErrors.NotFound(command.Id));

        var renamed = customer.Rename(command.Name);
        if (renamed.IsFailure)
            return renamed;

        // A stale version fails the save with ConflictException (persistence.concurrency_conflict).
        await customers.UpdateAsync(customer, command.ExpectedVersion, cancellationToken);
        return Result.Success();
    }
}
