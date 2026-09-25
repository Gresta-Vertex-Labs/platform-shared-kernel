using SharedKernel.Execution.Tenancy;
using SharedKernel.Security.Mtls.Validation;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Validation;

public sealed class MtlsValidationResultTests
{
    [Fact]
    public void Success_AllValues_ArePreserved()
    {
        TenantId tenantId = new TenantId(Guid.NewGuid());

        MtlsValidationResult result = MtlsValidationResult.Success("client", tenantId, ["admin"], ["payments:initiate"]);

        Assert.True(result.IsValid);
        Assert.Equal("client", result.ClientId);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(["admin"], result.Roles);
        Assert.Equal(["payments:initiate"], result.Permissions);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Success_OnlyClientId_DefaultsToEmptyGrants()
    {
        MtlsValidationResult result = MtlsValidationResult.Success("client");

        Assert.Null(result.TenantId);
        Assert.Empty(result.Roles);
        Assert.Empty(result.Permissions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Success_BlankClientId_Throws(string clientId)
    {
        Assert.Throws<ArgumentException>(() => MtlsValidationResult.Success(clientId));
    }

    [Fact]
    public void Success_EmptyTenantId_Throws()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => MtlsValidationResult.Success("client", default(TenantId)));

        Assert.Equal("tenantId", exception.ParamName);
    }

    [Fact]
    public void Failure_Defaults_AreRejectedWithoutIdentity()
    {
        MtlsValidationResult result = MtlsValidationResult.Failure();

        Assert.False(result.IsValid);
        Assert.Equal("Rejected", result.FailureReason);
        Assert.Null(result.ClientId);
        Assert.Null(result.TenantId);
        Assert.Empty(result.Roles);
        Assert.Empty(result.Permissions);
    }

    [Fact]
    public void Failure_Reason_IsPreserved()
    {
        Assert.Equal("UnknownClient", MtlsValidationResult.Failure("UnknownClient").FailureReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Failure_BlankReason_Throws(string reason)
    {
        Assert.Throws<ArgumentException>(() => MtlsValidationResult.Failure(reason));
    }
}
