using FluentAssertions;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Reporting.Abstractions.Validation;

namespace SharedKernel.Reporting.Abstractions.Tests.Validation;

public sealed class ReportExportPreconditionsTests
{
    private static readonly ReportDefinition<string> Definition = new()
    {
        Columns = [new ReportColumn<string> { Header = "Value", Ordinal = 0, ValueSelector = r => r }],
    };

    [Theory]
    [InlineData(null)]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    public void ValidateDefinitionAndDestination_ValidDestination_Succeeds(string? tenantId)
    {
        var destination = new ReportDestination
        {
            Store = "reports",
            TenantId = tenantId is null ? null : TenantId.Parse(tenantId),
            Key = "export.csv",
        };

        ReportExportPreconditions.ValidateDefinitionAndDestination(Definition, destination).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "export.csv", "Store is required")]
    [InlineData("reports", " ", "Key is required")]
    public void ValidateDefinitionAndDestination_InvalidDestination_ReturnsInvalidDestination(
        string store,
        string key,
        string expectedMessage)
    {
        var destination = new ReportDestination { Store = store, Key = key };

        var result = ReportExportPreconditions.ValidateDefinitionAndDestination(Definition, destination);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reporting.invalid_destination");
        result.Error.Message.Should().Contain(expectedMessage);
    }

    [Fact]
    public void ValidateDefinitionAndDestination_DefaultTenant_ReturnsInvalidDestination()
    {
        var destination = new ReportDestination { Store = "reports", TenantId = default(TenantId), Key = "export.csv" };

        var result = ReportExportPreconditions.ValidateDefinitionAndDestination(Definition, destination);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reporting.invalid_destination");
        result.Error.Message.Should().Contain("TenantId");
    }
}
