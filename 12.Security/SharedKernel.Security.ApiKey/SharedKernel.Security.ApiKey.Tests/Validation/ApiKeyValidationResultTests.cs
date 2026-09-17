using SharedKernel.Security.ApiKey.Validation;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Validation;

public sealed class ApiKeyValidationResultTests
{
    [Fact]
    public void Success_AllValues_ArePreserved()
    {
        var tenantId = Guid.NewGuid();

        ApiKeyValidationResult result = ApiKeyValidationResult.Success("client", tenantId, ["admin"], ["orders:read"], "KEYID");

        Assert.True(result.IsValid);
        Assert.Equal("client", result.ClientId);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(["admin"], result.Roles);
        Assert.Equal(["orders:read"], result.Permissions);
        Assert.Equal("KEYID", result.KeyId);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Success_OnlyClientId_DefaultsToEmptyGrants()
    {
        ApiKeyValidationResult result = ApiKeyValidationResult.Success("client");

        Assert.Null(result.TenantId);
        Assert.Empty(result.Roles);
        Assert.Empty(result.Permissions);
        Assert.Null(result.KeyId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Success_BlankClientId_Throws(string clientId)
    {
        Assert.Throws<ArgumentException>(() => ApiKeyValidationResult.Success(clientId));
    }

    [Fact]
    public void Success_NullClientId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ApiKeyValidationResult.Success(null!));
    }

    [Fact]
    public void Success_EmptyTenantId_Throws()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => ApiKeyValidationResult.Success("client", Guid.Empty));

        Assert.Equal("tenantId", exception.ParamName);
    }

    [Fact]
    public void Failure_Defaults_AreRejectedWithoutIdentity()
    {
        ApiKeyValidationResult result = ApiKeyValidationResult.Failure();

        Assert.False(result.IsValid);
        Assert.Equal("Rejected", result.FailureReason);
        Assert.Null(result.ClientId);
        Assert.Null(result.TenantId);
        Assert.Null(result.KeyId);
        Assert.Empty(result.Roles);
        Assert.Empty(result.Permissions);
    }

    [Fact]
    public void Failure_ReasonAndKeyId_ArePreserved()
    {
        ApiKeyValidationResult result = ApiKeyValidationResult.Failure("Expired", "KEYID");

        Assert.Equal("Expired", result.FailureReason);
        Assert.Equal("KEYID", result.KeyId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Failure_BlankReason_Throws(string reason)
    {
        Assert.Throws<ArgumentException>(() => ApiKeyValidationResult.Failure(reason));
    }
}
