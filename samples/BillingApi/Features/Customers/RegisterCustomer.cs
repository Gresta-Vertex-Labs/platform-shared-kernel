using BillingApi.Domain;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Authorization;
using SharedKernel.Execution.Context;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Customers;

[RequirePermission(Permissions.Write)]
public sealed record RegisterCustomer(CustomerId Id, string Name, string Email, string? TaxNumber)
    : ICommand<CustomerId>, IAuditableRequest<Result<CustomerId>>
{
    public string Action => "customer.registered";
    public string ResourceType => nameof(Customer);
    public string ResourceId => Id.Value.ToString();
    public string? BeforeSnapshot => null;
    public string? GetAfterSnapshot(Result<CustomerId> response) => response.IsSuccess ? $$"""{"name":"{{Name}}"}""" : null;
}

public sealed class RegisterCustomerHandler(
    IRepository<Customer, CustomerId> customers,
    ICustomerDirectory directory,
    IRequestContext caller,
    IClock clock)
    : ICommandHandler<RegisterCustomer, CustomerId>
{
    public async Task<Result<CustomerId>> Handle(RegisterCustomer command, CancellationToken cancellationToken)
    {
        // Runs inside TransactionBehavior's retrying transaction: everything it needs is loaded here, so a
        // replay after a transient fault starts from scratch.
        if (await directory.FindByEmailAsync(command.Email, cancellationToken) is not null)
            return Result<CustomerId>.Failure(Error.Conflict("customer.email.taken", "A customer with this email already exists."));

        var customer = Customer.Register(command.Id, caller.TenantId!.Value, command.Name, command.Email, command.TaxNumber, clock);
        if (customer.IsFailure)
            return Result<CustomerId>.Failure(customer.Error);

        await customers.AddAsync(customer.Value, cancellationToken);
        return Result<CustomerId>.Success(customer.Value.Id); // no SaveChanges: TransactionBehavior saves and commits
    }
}
