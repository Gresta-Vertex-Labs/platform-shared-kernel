using BillingApi.Domain;
using BillingApi.Features.Customers;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore;

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
