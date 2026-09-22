using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Reporting.Spreadsheet.Exporters;
using SharedKernel.Reporting.Spreadsheet.Options;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Spreadsheet.Tests.Exporters;

public sealed class SpreadsheetReportExporterTests
{
    [Fact]
    public async Task ExportToStreamAsync_SourceEnumeratedExactlyOnce_ProducesCorrectWorkbook()
    {
        // T-07: proves D-07's "no List<TRow> buffering" claim structurally — a source that throws
        // on a second enumeration attempt still produces a correct workbook.
        var exporter = CreateExporter();
        var definition = ThreeColumnDefinition();
        var rows = new SingleEnumerationAsyncEnumerable<TestRow>(
        [
            new TestRow(1, "Alice", 10.5m, new DateTime(2026, 1, 1)),
            new TestRow(2, "Bob", 20.25m, new DateTime(2026, 2, 1)),
        ]);

        using var destination = new MemoryStream();
        var result = await exporter.ExportToStreamAsync(rows, definition, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        destination.Position = 0;
        using var workbook = new XLWorkbook(destination);
        var worksheet = workbook.Worksheets.First();
        worksheet.Cell(3, 1).GetString().Should().Be("2");
    }

    [Fact]
    public async Task ExportToStreamAsync_OutputCorrectness_HeaderOrdinalOrderAndValuesRoundTrip()
    {
        var exporter = CreateExporter();
        var definition = new ReportDefinition<TestRow>
        {
            Culture = System.Globalization.CultureInfo.InvariantCulture,
            Columns =
            [
                new ReportColumn<TestRow> { Header = "Amount", Ordinal = 2, ValueSelector = r => r.Amount },
                new ReportColumn<TestRow> { Header = "Id", Ordinal = 0, ValueSelector = r => r.Id },
                new ReportColumn<TestRow> { Header = "Name", Ordinal = 1, ValueSelector = r => r.Name },
            ],
        };

        var rows = Rows(new TestRow(7, "Carol", 99.5m, new DateTime(2026, 5, 1)));

        using var destination = new MemoryStream();
        var result = await exporter.ExportToStreamAsync(rows, definition, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        destination.Position = 0;
        using var workbook = new XLWorkbook(destination);
        var worksheet = workbook.Worksheets.First();

        // Ordinal-respecting order: Id(0), Name(1), Amount(2) — regardless of declaration order above.
        worksheet.Cell(1, 1).GetString().Should().Be("Id");
        worksheet.Cell(1, 2).GetString().Should().Be("Name");
        worksheet.Cell(1, 3).GetString().Should().Be("Amount");

        worksheet.Cell(2, 1).GetString().Should().Be("7");
        worksheet.Cell(2, 2).GetString().Should().Be("Carol");
        worksheet.Cell(2, 3).GetString().Should().Be("99.5");
    }

    [Fact]
    public async Task ExportToStreamAsync_NullValue_WritesBlankCell()
    {
        var exporter = CreateExporter();
        var definition = new ReportDefinition<TestRow>
        {
            Columns = [new ReportColumn<TestRow> { Header = "Name", Ordinal = 0, ValueSelector = r => r.Name }],
        };
        var rows = Rows(new TestRow(1, null!, 0m, DateTime.UnixEpoch));

        using var destination = new MemoryStream();
        var result = await exporter.ExportToStreamAsync(rows, definition, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        destination.Position = 0;
        using var workbook = new XLWorkbook(destination);
        var worksheet = workbook.Worksheets.First();
        worksheet.Cell(2, 1).GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task ExportToStreamAsync_EmptyColumns_ReturnsValidationFailure()
    {
        var exporter = CreateExporter();
        var definition = new ReportDefinition<TestRow> { Columns = [] };

        using var destination = new MemoryStream();
        var result = await exporter.ExportToStreamAsync(Rows(), definition, destination, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reporting.empty_columns");
    }

    [Fact]
    public async Task ExportAsync_DeliversThroughStorage()
    {
        var fileStorage = new InMemoryFileStorage("reports");
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);
        var exporter = new SpreadsheetReportExporter<TestRow>(writer, Microsoft.Extensions.Options.Options.Create(new SpreadsheetExportOptions()));
        var definition = ThreeColumnDefinition();
        var destination = new ReportDestination { Store = "reports", Key = "export.xlsx" };

        var result = await exporter.ExportAsync(
            Rows(new TestRow(1, "Alice", 1m, DateTime.UnixEpoch)),
            definition,
            destination,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowCount.Should().Be(1);
        fileStorage.WasUploaded("export.xlsx").Should().BeTrue();
    }

    private static ReportDefinition<TestRow> ThreeColumnDefinition() => new()
    {
        Columns =
        [
            new ReportColumn<TestRow> { Header = "Id", Ordinal = 0, ValueSelector = r => r.Id },
            new ReportColumn<TestRow> { Header = "Name", Ordinal = 1, ValueSelector = r => r.Name },
            new ReportColumn<TestRow> { Header = "Amount", Ordinal = 2, ValueSelector = r => r.Amount },
        ],
    };

    private static async IAsyncEnumerable<TestRow> Rows(params TestRow[] rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }

    private static SpreadsheetReportExporter<TestRow> CreateExporter()
    {
        var fileStorage = new InMemoryFileStorage("reports");
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);
        var options = Microsoft.Extensions.Options.Options.Create(new SpreadsheetExportOptions());
        return new SpreadsheetReportExporter<TestRow>(writer, options);
    }
}
