using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Reporting.Pdf.Exporters;
using SharedKernel.Reporting.Pdf.Options;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Pdf.Tests.Exporters;

public sealed class PdfReportExporterTests
{
    [Fact]
    public async Task ExportToStreamAsync_SourceEnumeratedExactlyOnce_ProducesValidPdf()
    {
        // T-09: mirrors the Spreadsheet provider's single-pass proof.
        var exporter = CreateExporter();
        var definition = ThreeColumnDefinition();
        var rows = new SingleEnumerationAsyncEnumerable<TestRow>(
        [
            new TestRow(1, "Alice", 10.5m),
            new TestRow(2, "Bob", 20.25m),
        ]);

        using var destination = new MemoryStream();
        var result = await exporter.ExportToStreamAsync(rows, definition, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        destination.Length.Should().BeGreaterThan(0);

        destination.Position = 0;
        using var pdf = PdfReader.Open(destination, PdfDocumentOpenMode.Import);
        pdf.PageCount.Should().Be(1);
    }

    [Fact]
    public async Task ExportToStreamAsync_ManyRows_PaginatesAcrossMultiplePagesWithNoRowsDropped()
    {
        // T-10: the multi-page / page-break proof.
        var exporter = CreateExporter();
        var definition = ThreeColumnDefinition();
        const int rowCount = 400;
        var rows = GenerateRows(rowCount);

        using var destination = new MemoryStream();
        var result = await exporter.ExportToStreamAsync(rows, definition, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        destination.Position = 0;
        using var pdf = PdfReader.Open(destination, PdfDocumentOpenMode.Import);
        pdf.PageCount.Should().BeGreaterThan(1, "400 rows must force MigraDoc's table renderer across more than one page");
    }

    [Fact]
    public async Task ExportToStreamAsync_RowCount_MatchesEncodedRows()
    {
        var definition = ThreeColumnDefinition();
        const int rowCount = 250;

        var fileStorage = new InMemoryFileStorage("reports");
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);
        var exporterForOutcome = new PdfReportExporter<TestRow>(writer, Microsoft.Extensions.Options.Options.Create(new PdfExportOptions()));
        var outcome = await exporterForOutcome.ExportAsync(
            GenerateRows(rowCount),
            definition,
            new ReportDestination { Store = "reports", Key = "export.pdf" },
            CancellationToken.None);

        outcome.IsSuccess.Should().BeTrue();
        outcome.Value.RowCount.Should().Be(rowCount);
    }

    [Fact]
    public async Task ExportToStreamAsync_EmptyColumns_ReturnsValidationFailure()
    {
        var exporter = CreateExporter();
        var definition = new ReportDefinition<TestRow> { Columns = [] };

        using var destination = new MemoryStream();
        var result = await exporter.ExportToStreamAsync(GenerateRows(1), definition, destination, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reporting.empty_columns");
    }

    [Fact]
    public async Task ExportAsync_DeliversThroughStorage()
    {
        var fileStorage = new InMemoryFileStorage("reports");
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);
        var exporter = new PdfReportExporter<TestRow>(writer, Microsoft.Extensions.Options.Options.Create(new PdfExportOptions()));
        var definition = ThreeColumnDefinition() with { Title = "Statement" };
        var destination = new ReportDestination { Store = "reports", Key = "export.pdf" };

        var result = await exporter.ExportAsync(GenerateRows(5), definition, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowCount.Should().Be(5);
        fileStorage.WasUploaded("export.pdf").Should().BeTrue();
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

    private static async IAsyncEnumerable<TestRow> GenerateRows(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Yield();
            yield return new TestRow(i, $"Row {i}", i * 1.1m);
        }
    }

    private static PdfReportExporter<TestRow> CreateExporter()
    {
        var fileStorage = new InMemoryFileStorage("reports");
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);
        var options = Microsoft.Extensions.Options.Options.Create(new PdfExportOptions());
        return new PdfReportExporter<TestRow>(writer, options);
    }
}
