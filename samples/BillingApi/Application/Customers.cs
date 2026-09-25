using BillingApi.Domain;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Execution.Context;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace BillingApi.Application;

public static class Permissions
{
    public const string Read = "billing.read";
    public const string Write = "billing.write";
    public const string Admin = "billing.admin";
}

public sealed record CustomerView(Guid Id, string Name, string Email, string? TaxNumber, int InvoiceCount, bool IsDeleted);

/// <summary>Reads an encrypted column by value — implemented over the blind index in the infrastructure layer.</summary>
public interface ICustomerDirectory
{
    Task<Customer?> FindByEmailAsync(string email, CancellationToken cancellationToken);
}

// ---------------------------------------------------------------------------------------------------------------
// Register
// ---------------------------------------------------------------------------------------------------------------

public sealed record RegisterCustomer(CustomerId Id, string Name, string Email, string? TaxNumber)
    : ICommand<CustomerId>, IAuthorizeRequest, IAuditableRequest<Result<CustomerId>>
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Write];
    public PermissionMatch PermissionMatch => PermissionMatch.All;

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

// ---------------------------------------------------------------------------------------------------------------
// Rename — optimistic concurrency with the client's If-Match version
// ---------------------------------------------------------------------------------------------------------------

public sealed record RenameCustomer(CustomerId Id, string Name, EntityVersion ExpectedVersion)
    : ICommand, IAuthorizeRequest, IAuditableRequest<Result>
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Write];
    public PermissionMatch PermissionMatch => PermissionMatch.All;

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

// ---------------------------------------------------------------------------------------------------------------
// Delete — soft delete (the row stays, filtered from every query)
// ---------------------------------------------------------------------------------------------------------------

public sealed record DeleteCustomer(CustomerId Id) : ICommand, IAuthorizeRequest, IAuditableRequest<Result>
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Write];
    public PermissionMatch PermissionMatch => PermissionMatch.All;

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

        await customers.DeleteAsync(customer, cancellationToken);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------------------------------------------

public sealed record GetCustomerByEmail(string Email) : IQuery<CustomerView>, IAuthorizeRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Read];
    public PermissionMatch PermissionMatch => PermissionMatch.All;
}

public sealed class GetCustomerByEmailHandler(ICustomerDirectory directory) : IQueryHandler<GetCustomerByEmail, CustomerView>
{
    public async Task<Result<CustomerView>> Handle(GetCustomerByEmail query, CancellationToken cancellationToken)
    {
        var customer = await directory.FindByEmailAsync(query.Email, cancellationToken);
        return customer is null
            ? Result<CustomerView>.Failure(Error.NotFound("customer.not_found", "No customer has this email."))
            : Result<CustomerView>.Success(customer.ToView());
    }
}

public static class CustomerErrors
{
    public static Error NotFound(CustomerId id) => Error.NotFound("customer.not_found", $"Customer {id.Value} was not found.");
}

public static class CustomerMapping
{
    public static CustomerView ToView(this Customer customer) =>
        new(customer.Id.Value, customer.Name, customer.Email, customer.TaxNumber, customer.InvoiceCount, customer.IsDeleted);
}
