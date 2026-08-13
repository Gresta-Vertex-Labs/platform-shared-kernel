using SharedKernel.Security.ApiKey.Validation;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Validation;

public sealed class ApiKeyValidationResultTests
{
    [Fact]
    public void Invalid_IsNotValid_AndCarriesNoData()
    {
        var result = ApiKeyValidationResult.Invalid;

        Assert.False(result.IsValid);
        Assert.Null(result.ClientId);
        Assert.Null(result.Roles);
        Assert.Null(result.Permissions);
    }

    [Fact]
    public void Valid_WithNoArguments_IsValidWithNullOptionalData()
    {
        var result = ApiKeyValidationResult.Valid();

        Assert.True(result.IsValid);
        Assert.Null(result.ClientId);
        Assert.Null(result.Roles);
        Assert.Null(result.Permissions);
    }

    [Fact]
    public void Valid_WithClientIdRolesAndPermissions_CarriesThemThrough()
    {
        var roles = new[] { "admin" };
        var permissions = new[] { "orders:read" };

        var result = ApiKeyValidationResult.Valid("client-123", roles, permissions);

        Assert.True(result.IsValid);
        Assert.Equal("client-123", result.ClientId);
        Assert.Same(roles, result.Roles);
        Assert.Same(permissions, result.Permissions);
    }
}
