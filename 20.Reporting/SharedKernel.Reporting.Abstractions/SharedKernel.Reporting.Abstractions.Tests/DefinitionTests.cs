using System.Globalization;
using FluentAssertions;
using SharedKernel.Reporting.Internal;

namespace SharedKernel.Reporting.Abstractions.Tests;

public sealed class DefinitionTests
{
    [Fact]
    public void Builder_KeepsColumnsInTheOrderTheyWereAdded()
    {
        ReportDefinition<Row> definition = ReportDefinition.For<Row>()
            .Title("Orders")
            .Culture(CultureInfo.GetCultureInfo("de-DE"))
            .Column("Id", r => r.Id)
            .Column("Amount", r => r.Amount, format: "N2", alignment: ReportColumnAlignment.Right, relativeWidth: 2)
            .Column("Name", r => r.Name)
            .Build();

        definition.Title.Should().Be("Orders");
        definition.Culture.Name.Should().Be("de-DE");
        definition.Columns.Select(c => c.Header).Should().Equal("Id", "Amount", "Name");
        definition.Columns[1].Format.Should().Be("N2");
        definition.Columns[1].Alignment.Should().Be(ReportColumnAlignment.Right);
        definition.Columns[1].RelativeWidth.Should().Be(2);
        definition.Columns[1].Value(new Row(1, "a", 3.5m)).Should().Be(3.5m);
    }

    [Fact]
    public void Builder_FormatterOverload_ReceivesTheTypedValueAndCulture()
    {
        ReportDefinition<Row> definition = ReportDefinition.For<Row>()
            .Column("Name", r => r.Name, (name, culture) => name.ToUpper(culture))
            .Build();

        ReportColumn<Row> column = definition.Columns[0];
        ReportValueFormatting.FormatColumnValue(column, column.Value(new Row(1, "abc", 0)), CultureInfo.InvariantCulture)
            .Should().Be("ABC");
    }

    [Fact]
    public void Builder_WithoutColumns_Throws()
    {
        var build = () => ReportDefinition.For<Row>().Build();

        build.Should().Throw<InvalidOperationException>().WithMessage("*at least one column*");
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public void Validator_RejectsANonPositiveRelativeWidth(double width)
    {
        var definition = new ReportDefinition<Row>
        {
            Columns = [new ReportColumn<Row> { Header = "Id", Value = r => r.Id, RelativeWidth = width }],
        };

        ReportRequestValidator.ValidateDefinition(definition).Error.Code.Should().Be(ReportingErrorCodes.InvalidDefinition);
    }

    [Fact]
    public void Validator_RejectsANullColumn()
    {
        var definition = new ReportDefinition<Row> { Columns = [null!] };

        ReportRequestValidator.ValidateDefinition(definition).Error.Code.Should().Be(ReportingErrorCodes.InvalidDefinition);
    }

    [Theory]
    [InlineData("", "k", null, "Store")]
    [InlineData("reports", " ", null, "Key")]
    [InlineData("reports", "k", "a/b.csv", "DownloadFileName")]
    [InlineData("reports", "k", "tab\there.csv", "DownloadFileName")]
    public void Validator_RejectsAnInvalidDestination(string store, string key, string? fileName, string mentioned)
    {
        var destination = new ReportDestination { Store = store, Key = key, DownloadFileName = fileName };

        var error = ReportRequestValidator.ValidateDestination(destination).Error;

        error.Code.Should().Be(ReportingErrorCodes.InvalidDestination);
        error.Message.Should().Contain(mentioned);
    }

    [Fact]
    public void Validator_RejectsADefaultTenantAndANonPositiveExpiry()
    {
        ReportRequestValidator.ValidateDestination(new ReportDestination { Store = "r", Key = "k", TenantId = default(TenantId) })
            .IsFailure.Should().BeTrue();
        ReportRequestValidator.ValidateDestination(new ReportDestination { Store = "r", Key = "k", PresignedDownloadUrlExpiry = TimeSpan.Zero })
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Format_NormalizesNameAndExtension_AndComparesByName()
    {
        var format = new ReportFormat("JSONL", "application/x-ndjson", ".JSONL");

        format.Name.Should().Be("jsonl");
        format.FileExtension.Should().Be(".jsonl");
        format.Should().Be(new ReportFormat("jsonl", "text/plain", ".txt"));
        format.WithExtension("orders").Should().Be("orders.jsonl");
        format.WithExtension("orders.JSONL").Should().Be("orders.JSONL");
    }

    [Theory]
    [InlineData("has space", "text/plain", ".txt")]
    [InlineData("csv", "text/csv", "csv")]
    [InlineData("csv", " ", ".csv")]
    public void Format_RejectsMalformedValues(string name, string contentType, string extension)
    {
        var create = () => new ReportFormat(name, contentType, extension);

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null, "en-US", null, null)]
    [InlineData(true, "en-US", null, "True")]
    [InlineData(1234.5, "de-DE", "N2", "1.234,50")]
    [InlineData(1234.5, "en-US", null, "1234.5")]
    [InlineData("=cmd", "en-US", "N2", "=cmd")]
    public void ValueFormatting_FormatsByCultureAndFormat(object? value, string culture, string? format, string? expected)
    {
        ReportValueFormatting.Format(value, CultureInfo.GetCultureInfo(culture), format).Should().Be(expected);
    }

    [Fact]
    public void ValueFormatting_FormatsDates()
    {
        var date = new DateTime(2026, 9, 26, 14, 5, 0, DateTimeKind.Utc);

        ReportValueFormatting.Format(date, CultureInfo.InvariantCulture, "yyyy-MM-dd HH:mm").Should().Be("2026-09-26 14:05");
        ReportValueFormatting.Format(new DateOnly(2026, 9, 26), CultureInfo.GetCultureInfo("tr-TR")).Should().Be("26.09.2026");
    }
}
