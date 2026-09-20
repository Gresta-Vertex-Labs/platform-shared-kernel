using FluentAssertions;
using SharedKernel.Persistence.PostgreSQL.Conventions;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Conventions;

/// <summary>
/// T-38 (1): SnakeCaseNamingConvention — verifies snake_case conversion logic.
/// </summary>
public sealed class SnakeCaseNamingConventionTests
{
    [Theory]
    [InlineData("UserName", "user_name")]
    [InlineData("CreatedOn", "created_on")]
    [InlineData("OrderItems", "order_items")]
    [InlineData("HTTPSUrl", "https_url")]
    [InlineData("HTMLParser", "html_parser")]
    [InlineData("user_name", "user_name")] // already snake_case — idempotent
    [InlineData("id", "id")] // already lowercase
    [InlineData("IsDeleted", "is_deleted")]
    [InlineData("TenantId", "tenant_id")]
    [InlineData("", "")] // empty passes through unchanged
    public void ToSnakeCase_Converts_PascalCase_To_SnakeCase(string input, string expected)
    {
        var result = SnakeCaseNamingConvention.ToSnakeCase(input);
        result.Should().Be(expected);
    }
}
