using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Pdf.Fonts;
using SharedKernel.Storage;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Pdf.Tests;

public sealed class PdfReportExporterTests
{
    private static readonly ReportDefinition<TestRow> Simple = ReportDefinition.For<TestRow>()
        .Title("Orders")
        .Column("Id", r => r.Id)
        .Column("Name", r => r.Name, relativeWidth: 3)
        .Column("Amount", r => r.Amount, format: "N2")
        .Build();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManyColumns_AlwaysFitThePage(bool landscape)
    {
        ReportDefinitionBuilder<TestRow> builder = ReportDefinition.For<TestRow>();
        for (var i = 0; i < 12; i++)
        {
            builder.Column($"Column {i}", r => r.Name, relativeWidth: i % 3 + 1);
        }

        var options = new PdfExportOptions { Landscape = landscape };
        var document = new PdfTableDocument<TestRow>(builder.Build(), options);
        document.AddRow(new TestRow(1, "value", 1, DateTime.UnixEpoch));

        PdfFontResolverRegistration.EnsureRegistered();
        var renderer = new PdfDocumentRenderer { Document = document.Document };
        renderer.RenderDocument();
        RenderInfo table = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.Single(i => i.DocumentObject is Table);

        double pageWidth = landscape ? 297 : 210;
        double right = table.LayoutInfo.ContentArea.X.Millimeter + table.LayoutInfo.ContentArea.Width.Millimeter;
        right.Should().BeLessThanOrEqualTo(pageWidth - options.MarginMillimeters + 0.5);
        table.LayoutInfo.ContentArea.Width.Millimeter.Should().BeApproximately(pageWidth - 2 * options.MarginMillimeters, 1);
    }

    [Fact]
    public void Numbers_AreRightAligned_TheRestLeft_UnlessTheColumnSaysOtherwise()
    {
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>()
            .Column("Amount", r => r.Amount)
            .Column("Name", r => r.Name)
            .Column("Centered", r => r.Id, alignment: ReportColumnAlignment.Center)
            .Build();
        var document = new PdfTableDocument<TestRow>(definition, new PdfExportOptions());

        document.AddRow(new TestRow(1, "x", 2, DateTime.UnixEpoch));

        Row row = document.Document.Sections[0]!.LastTable!.Rows[1]!;
        Alignment(row, 0).Should().Be(ParagraphAlignment.Right);
        Alignment(row, 1).Should().Be(ParagraphAlignment.Left);
        Alignment(row, 2).Should().Be(ParagraphAlignment.Center);
        document.Document.Sections[0]!.Footers.Primary.Elements.Count.Should().Be(1, "the page-number footer is on by default");
    }

    [Fact]
    public async Task ManyRows_PaginateWithEveryPageNumbered_AndTheTitleIsMetadata()
    {
        using var stream = new MemoryStream();

        Result<ReportStreamOutcome> result = await Exporter().ExportToStreamAsync(Rows(400), Simple, stream);

        result.Value.RowCount.Should().Be(400);
        result.Value.SizeBytes.Should().Be(stream.Length);
        stream.Position = 0;
        using PdfDocument pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        pdf.PageCount.Should().BeGreaterThan(1);
        pdf.Info.Title.Should().Be("Orders");
    }

    [Fact]
    public async Task EnumeratesTheRowsOnce()
    {
        using var stream = new MemoryStream();
        var rows = new SingleEnumerationAsyncEnumerable<TestRow>([new TestRow(1, "a", 1, DateTime.UnixEpoch)]);

        (await Exporter().ExportToStreamAsync(rows, Simple, stream)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_StoresThePdf()
    {
        var store = new InMemoryFileStorage("reports");

        Result<ReportExportOutcome> result = await Exporter(storageFactory: InMemoryStorage.CreateFactory(store))
            .ExportAsync(Rows(5), Simple, new ReportDestination { Store = "reports", Key = "orders.pdf" });

        result.Value.RowCount.Should().Be(5);
        (await store.GetPropertiesAsync("orders.pdf")).Value.ContentType.Should().Be("application/pdf");
        using PdfDocument pdf = PdfReader.Open(new MemoryStream(store.GetContent("orders.pdf")), PdfDocumentOpenMode.Import);
        pdf.PageCount.Should().Be(1);
    }

    [Fact]
    public async Task MoreRowsThanMaxRows_FailsAndStoresNothing()
    {
        var store = new InMemoryFileStorage("reports");

        Result<ReportExportOutcome> result = await Exporter(o => o.MaxRows = 3, InMemoryStorage.CreateFactory(store))
            .ExportAsync(Rows(4), Simple, new ReportDestination { Store = "reports", Key = "big.pdf" });

        result.Error.Code.Should().Be(ReportingErrorCodes.RowLimitExceeded);
        store.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task AddPdf_RegistersTheExporterForEveryRowType_AndValidatesOptionsAtStartup()
    {
        using (IHost host = Host(new Dictionary<string, string?> { ["SharedKernel:Reporting:Pdf:PaperSize"] = "Letter" }))
        {
            await host.StartAsync();
            host.Services.GetRequiredService<IPdfReportExporter<TestRow>>().Format.Should().Be(ReportFormat.Pdf);
            host.Services.GetRequiredService<IOptions<PdfExportOptions>>().Value.PaperSize.Should().Be(PdfPaperSize.Letter);
        }

        using IHost invalid = Host(new Dictionary<string, string?> { ["SharedKernel:Reporting:Pdf:FontSize"] = "100" });
        var start = () => invalid.StartAsync();
        await start.Should().ThrowAsync<OptionsValidationException>().WithMessage("*FontSize*");
    }

    private static ParagraphAlignment Alignment(Row row, int cell) =>
        ((Paragraph)row.Cells[cell].Elements[0]!).Format.Alignment;

    private static IHost Host(Dictionary<string, string?> settings)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSharedKernelReporting().AddPdf(builder.Configuration);
        return builder.Build();
    }

    private static PdfReportExporter<TestRow> Exporter(Action<PdfExportOptions>? configure = null, IFileStorageFactory? storageFactory = null)
    {
        var options = new PdfExportOptions();
        configure?.Invoke(options);
        return new PdfReportExporter<TestRow>(new ReportingDependencies(storageFactory: storageFactory), Options.Create(options));
    }

    private static async IAsyncEnumerable<TestRow> Rows(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Yield();
            yield return new TestRow(i, $"Customer {i} Şirketi", i * 10.5m, DateTime.UnixEpoch);
        }
    }
}
