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
    [InlineData("tenant-a")]
    public void ValidateDefinitionAndDestination_ValidDestination_Succeeds(string? tenantId)
    {
        var destination = new ReportDestination { Store = "reports", TenantId = tenantId, Key = "export.csv" };

        ReportExportPreconditions.ValidateDefinitionAndDestination(Definition, destination).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "export.csv", null, "Store is required")]
    [InlineData("reports", " ", null, "Key is required")]
    [InlineData("reports", "export.csv", "tenant/../b", "TenantId")]
    public void ValidateDefinitionAndDestination_InvalidDestination_ReturnsInvalidDestination(
        string store,
        string key,
        string? tenantId,
        string expectedMessage)
    {
        var destination = new ReportDestination { Store = store, TenantId = tenantId, Key = key };

        var result = ReportExportPreconditions.ValidateDefinitionAndDestination(Definition, destination);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reporting.invalid_destination");
        result.Error.Message.Should().Contain(expectedMessage);
    }
}
