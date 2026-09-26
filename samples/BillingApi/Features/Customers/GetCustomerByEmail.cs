using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Customers;

[RequirePermission(Permissions.Read)]
public sealed record GetCustomerByEmail(string Email) : IQuery<CustomerView>;

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
