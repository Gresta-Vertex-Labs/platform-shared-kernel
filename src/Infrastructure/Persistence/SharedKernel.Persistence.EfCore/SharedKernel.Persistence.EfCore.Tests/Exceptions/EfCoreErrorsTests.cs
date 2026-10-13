using FluentAssertions;
using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Persistence.EfCore.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Tests.Exceptions;

/// <summary>Every error the EF Core package builds is pinned: code, <see cref="ErrorType"/> and the shape of the message.</summary>
public sealed class EfCoreErrorsTests
{
    [Fact]
    public void ConcurrencyConflict_IsAConflict_WithTheRegistryCode_AndNamesTheEntityOnly()
    {
        var error = EfCoreErrors.ConcurrencyConflict("Order");

        error.Code.Should().Be("persistence.concurrency_conflict").And.Be(PersistenceErrorCodes.Concurrency.Conflict);
        error.Type.Should().Be(ErrorType.Conflict);
        error.Message.Should().Be("'Order' was changed or deleted by someone else. Reload it and retry.");
    }

    [Fact]
    public void OtherTenant_IsForbidden_WithTheRegistryCode_AndNamesTheRemedy()
    {
        var error = EfCoreErrors.OtherTenant("Order");

        error.Code.Should().Be("persistence.tenant_isolation_violation");
        error.Type.Should().Be(ErrorType.Forbidden);
        error.Message.Should().EndWith(".").And.Contain("'Order'").And.Contain("ICrossTenantScope.Enter(reason)");
    }

    [Fact]
    public void TenantChanged_IsForbidden_WithTheSameCode_AsAnotherTenant()
    {
        var error = EfCoreErrors.TenantChanged("Order");

        error.Code.Should().Be(EfCoreErrors.OtherTenant("Order").Code);
        error.Type.Should().Be(ErrorType.Forbidden);
        error.Message.Should().EndWith(".").And.Contain("must not move between tenants");
    }

    [Fact]
    public void BulkSpecificationNotSupported_IsValidation_WithTheNotSupportedReasonWord()
    {
        var error = EfCoreErrors.BulkSpecificationNotSupported("Includes are not supported; remove them from the specification.");

        error.Code.Should().Be("persistence.bulk_mutation.specification_not_supported");
        error.Type.Should().Be(ErrorType.Validation);
        error.Message.Should().Be(
            "The specification is not supported by bulk mutation operations: Includes are not supported; remove them from the specification.");
    }
}
