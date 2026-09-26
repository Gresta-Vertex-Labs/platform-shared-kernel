using BillingApi.Domain;
using BillingApi.Features;
using BillingApi.Features.Customers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Transactions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Testing;
using SharedKernel.Testing.Execution;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace BillingApi.Tests;

/// <summary>
/// A unit test of a command handler over the in-memory fakes of SharedKernel.Persistence.Testing — no database.
/// </summary>
public sealed class RegisterCustomerHandlerTests
{
    private sealed class NoCustomers : ICustomerDirectory
    {
        public Task<Customer?> FindByEmailAsync(string email, CancellationToken cancellationToken) => Task.FromResult<Customer?>(null);
    }

    [Fact]
    public async Task TheHandler_IsReRunnable_UnderTheRetryingUnitOfWork()
    {
        var tenant = new TenantId(Guid.NewGuid());
        var services = new ServiceCollection();
        var customers = services.AddFakeRepository<Customer, CustomerId>();
        var unitOfWork = services.AddFakeUnitOfWork();
        services.AddTestRequestContext(TestRequestContext.ForTenant(tenant).WithPermissions(Permissions.Write));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICustomerDirectory, NoCustomers>();
        services.AddScoped<RegisterCustomerHandler>();
        await using var provider = services.BuildServiceProvider();

        // Two transient failures: the operation runs three times, as the retrying execution strategy would.
        unitOfWork.TransientFailures = 2;
        var command = new RegisterCustomer(CustomerId.New(), "Ada", "ada@example.com", null);
        var result = await provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            ct => provider.GetRequiredService<RegisterCustomerHandler>().Handle(command, ct));

        result.IsSuccess.Should().BeTrue();
        unitOfWork.CommitCount.Should().Be(1);
        customers.Items.Should().ContainSingle().Which.Value.TenantId.Should().Be(tenant);
    }
}
