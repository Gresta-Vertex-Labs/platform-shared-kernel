using FluentAssertions;
using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.Abstractions.Tests.Errors;

/// <summary>The shared no-tenant <see cref="Error"/> every persistence package must raise identically.</summary>
public sealed class PersistenceSharedErrorsTests
{
    [Fact]
    public void TenantRequired_IsPinnedLiterally()
    {
        var error = PersistenceSharedErrors.TenantRequired();

        error.Code.Should().Be("persistence.tenant_required");
        error.Type.Should().Be(ErrorType.Forbidden);
        error.Message.Should().Be(
            "The request has no tenant. Resolve the tenant for this call, or run it inside a cross-tenant scope.");
    }

    [Fact]
    public void TenantRequired_IsEqualOnEveryCall()
    {
        PersistenceSharedErrors.TenantRequired().Should().Be(PersistenceSharedErrors.TenantRequired());
    }
}
