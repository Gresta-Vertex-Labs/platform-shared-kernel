using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;
using SharedKernel.Storage;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Reporting;

namespace SharedKernel.Reporting.Testing.Tests.Reporting;

public sealed class InMemoryReportExporterTests
{
    private static readonly ReportDefinition<Row> Definition = ReportDefinition.For<Row>()
        .Column("Id", r => r.Id)
        .Column("Name", r => r.Name)
        .Build();

    [Fact]
    public async Task ExportAsync_RecordsRowsDefinitionAndDestination_AndFabricatesTheOutcome()
    {
        var clock = new FakeClock();
        var exporter = new InMemoryReportExporter<Row>(ReportFormat.Xlsx, clock);
        var destination = new ReportDestination { Store = "reports", Key = "a.xlsx", PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(5) };

        Result<ReportExportOutcome> result = await exporter.ExportAsync(Rows(3), Definition, destination);

        exporter.LastRows.Should().HaveCount(3);
        exporter.LastDefinition.Should().BeSameAs(Definition);
        exporter.LastDestination.Should().BeSameAs(destination);
        result.Value.Format.Should().Be(ReportFormat.Xlsx);
        result.Value.RowCount.Should().Be(3);
        result.Value.StoredFile.Key.Should().Be("a.xlsx");
        result.Value.DownloadUrl!.ExpiresAt.Should().Be(clock.UtcNow + TimeSpan.FromMinutes(5));
        exporter.ShouldHaveExported(rows => rows[2].Name == "n3");
    }

    [Fact]
    public async Task ExportToStreamAsync_WritesATabSeparatedRendering()
    {
        var exporter = new InMemoryReportExporter<Row>();
        using var stream = new MemoryStream();

        Result<ReportStreamOutcome> result = await exporter.ExportToStreamAsync(Rows(2), Definition, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("Id\tName\n1\tn1\n2\tn2\n");
        result.Value.SizeBytes.Should().Be(stream.Length);
    }

    [Fact]
    public async Task SimulateFailure_ReturnsTheSimulatedError_AfterReadingTheRows()
    {
        var exporter = new InMemoryReportExporter<Row> { SimulateFailure = true };

        Result<ReportExportOutcome> result = await exporter.ExportAsync(Rows(2), Definition, new ReportDestination { Store = "reports", Key = "a" });

        result.Error.Code.Should().Be(StorageErrorCodes.Unavailable);
        exporter.LastRows.Should().HaveCount(2);
        exporter.Reset();
        exporter.SimulateFailure.Should().BeFalse();
        var assert = () => exporter.ShouldHaveExported();
        assert.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Factory_HandsOutOneExporterPerFormatAndRowType_AndParsesFormats()
    {
        var factory = new InMemoryReportExporterFactory();

        factory.GetExporter<Row>(ReportFormat.Pdf).Should().BeSameAs(factory.Exporter<Row>(ReportFormat.Pdf));
        factory.Exporter<Row>(ReportFormat.Pdf).Format.Should().Be(ReportFormat.Pdf);
        factory.ParseFormat(".XLSX").Value.Should().Be(ReportFormat.Xlsx);
        factory.ParseFormat("docx").Error.Code.Should().Be(ReportingErrorCodes.UnsupportedFormat);
        new InMemoryReportExporterFactory(ReportFormat.Csv).Formats.Should().Equal(ReportFormat.Csv);
    }

    [Fact]
    public async Task HtmlConverter_RecordsTheDocument_AndWritesAPlaceholderPdf()
    {
        var converter = new InMemoryHtmlToPdfConverter();
        using var stream = new MemoryStream();

        Result<long> result = await converter.ConvertToStreamAsync("<p>hi</p>", stream);
        Result<PdfDocumentOutcome> stored = await converter.ConvertAsync("<p>2</p>", new ReportDestination { Store = "docs", Key = "b.pdf" });

        result.Value.Should().Be(stream.Length);
        stream.ToArray().Should().Equal(InMemoryHtmlToPdfConverter.PlaceholderPdf.ToArray());
        stored.Value.StoredFile.Key.Should().Be("b.pdf");
        converter.Conversions.Select(c => c.Html).Should().Equal("<p>hi</p>", "<p>2</p>");
        converter.LastConversion!.Destination!.Store.Should().Be("docs");
    }

    [Fact]
    public async Task HtmlConverter_SimulateFailure_ReturnsTheSimulatedError_AndWritesNothing()
    {
        var converter = new InMemoryHtmlToPdfConverter { SimulateFailure = true };
        using var stream = new MemoryStream();

        (await converter.ConvertToStreamAsync("<p/>", stream)).Error.Code.Should().Be(ReportingErrorCodes.ConverterUnavailable);
        converter.SimulatedError = ReportingErrors.ConversionTimeout(TimeSpan.FromSeconds(1));
        (await converter.ConvertAsync("<p/>", new ReportDestination { Store = "d", Key = "k" })).Error.Code
            .Should().Be(ReportingErrorCodes.ConversionTimeout);

        stream.Length.Should().Be(0);
        converter.Conversions.Should().HaveCount(2);
    }

    [Fact]
    public void AddInMemoryReporting_ReplacesTheRealServices()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelReporting();
        services.AddInMemoryReporting();
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IReportExporterFactory>().Should().BeSameAs(provider.GetRequiredService<InMemoryReportExporterFactory>());
        provider.GetRequiredService<IHtmlToPdfConverter>().Should().BeSameAs(provider.GetRequiredService<InMemoryHtmlToPdfConverter>());
    }

    private static async IAsyncEnumerable<Row> Rows(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await Task.Yield();
            yield return new Row(i, $"n{i}");
        }
    }

    public sealed record Row(int Id, string Name);
}
