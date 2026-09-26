using System.Globalization;
using System.Runtime.CompilerServices;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Spreadsheet.Tests;

public sealed class SpreadsheetReportExporterTests
{
    private static readonly DateTime When = new(2026, 9, 26, 14, 5, 30, DateTimeKind.Unspecified);

    [Fact]
    public async Task WritesTypedCells_WithTranslatedNumberFormats()
    {
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>()
            .Column("Id", r => r.Id)
            .Column("Name", r => r.Name)
            .Column("Amount", r => r.Amount, format: "N2")
            .Column("When", r => r.When, format: "yyyy-MM-dd HH:mm")
            .Column("Day", r => DateOnly.FromDateTime(r.When))
            .Column("Active", r => r.Id > 0)
            .Column("Took", r => TimeSpan.FromMinutes(90))
            .Build();

        using XLWorkbook workbook = await ExportAsync([new TestRow(7, "Ada", 1234.5m, When)], definition);
        IXLWorksheet sheet = workbook.Worksheet(1);

        sheet.Cell("A2").DataType.Should().Be(XLDataType.Number);
        sheet.Cell("A2").GetValue<int>().Should().Be(7);
        sheet.Cell("B2").GetString().Should().Be("Ada");
        sheet.Cell("C2").DataType.Should().Be(XLDataType.Number);
        sheet.Cell("C2").GetValue<decimal>().Should().Be(1234.5m);
        sheet.Cell("C2").Style.NumberFormat.Format.Should().Be("#,##0.00");
        sheet.Cell("D2").DataType.Should().Be(XLDataType.DateTime);
        sheet.Cell("D2").GetDateTime().Should().Be(When);
        sheet.Cell("D2").Style.NumberFormat.Format.Should().Be("yyyy-mm-dd hh:mm");
        sheet.Cell("E2").GetDateTime().Should().Be(When.Date);
        sheet.Cell("E2").Style.NumberFormat.Format.Should().Be("yyyy-mm-dd");
        sheet.Cell("F2").DataType.Should().Be(XLDataType.Boolean);
        sheet.Cell("G2").GetTimeSpan().Should().Be(TimeSpan.FromMinutes(90));
        sheet.Cell("G2").Style.NumberFormat.Format.Should().Be("[h]:mm:ss");
    }

    [Fact]
    public async Task TextThatLooksLikeAFormula_StaysText()
    {
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>().Column("Name", r => r.Name).Build();

        using XLWorkbook workbook = await ExportAsync([new TestRow(1, "=HYPERLINK(\"http://evil\",\"x\")", 0, When)], definition);
        IXLCell cell = workbook.Worksheet(1).Cell("A2");

        cell.HasFormula.Should().BeFalse();
        cell.GetString().Should().Be("=HYPERLINK(\"http://evil\",\"x\")");
    }

    [Fact]
    public async Task AColumnWithAFormatter_IsText_AndNullIsAnEmptyCell()
    {
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>()
            .Column("Amount", r => r.Amount, (amount, culture) => amount.ToString("0.0", culture) + " TL")
            .Column("Empty", r => (string?)null)
            .Build();

        using XLWorkbook workbook = await ExportAsync([new TestRow(1, "x", 2.25m, When)], definition);
        IXLWorksheet sheet = workbook.Worksheet(1);

        sheet.Cell("A2").DataType.Should().Be(XLDataType.Text);
        sheet.Cell("A2").GetString().Should().Be("2.3 TL");
        sheet.Cell("B2").IsEmpty().Should().BeTrue();
    }

    [Fact]
    public async Task HeaderIsBoldFrozenAndFiltered_AndTheTitleNamesTheSheet()
    {
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>()
            .Title("Orders: Q3/2026 [final]")
            .Column("Id", r => r.Id)
            .Column("Name", r => r.Name, relativeWidth: 3)
            .Build();

        using XLWorkbook workbook = await ExportAsync([new TestRow(1, "x", 0, When)], definition);
        IXLWorksheet sheet = workbook.Worksheet(1);

        sheet.Name.Should().Be("Orders_ Q3_2026 _final_");
        workbook.Properties.Title.Should().Be("Orders: Q3/2026 [final]");
        sheet.Cell("A1").GetString().Should().Be("Id");
        sheet.Cell("A1").Style.Font.Bold.Should().BeTrue();
        sheet.SheetView.SplitRow.Should().Be(1);
        sheet.AutoFilter.IsEnabled.Should().BeTrue();
        sheet.Column(2).Width.Should().BeGreaterThan(sheet.Column(1).Width * 2);
    }

    [Fact]
    public async Task ExplicitAlignment_IsApplied()
    {
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>()
            .Column("Name", r => r.Name, alignment: ReportColumnAlignment.Center)
            .Build();

        using XLWorkbook workbook = await ExportAsync([new TestRow(1, "x", 0, When)], definition);

        workbook.Worksheet(1).Cell("A2").Style.Alignment.Horizontal.Should().Be(XLAlignmentHorizontalValues.Center);
    }

    [Fact]
    public async Task ExportAsync_StreamsIntoStorage_AndTheWorkbookOpens()
    {
        var store = new InMemoryFileStorage("reports");
        SpreadsheetReportExporter<TestRow> exporter = Exporter(storageFactory: InMemoryStorage.CreateFactory(store));

        Result<ReportExportOutcome> result = await exporter.ExportAsync(
            Many(2_000),
            ReportDefinition.For<TestRow>().Column("Id", r => r.Id).Column("Name", r => r.Name).Build(),
            new ReportDestination { Store = "reports", Key = "orders.xlsx" });

        result.Value.RowCount.Should().Be(2_000);
        result.Value.SizeBytes.Should().Be(store.GetContent("orders.xlsx").Length);
        (await store.GetPropertiesAsync("orders.xlsx")).Value.ContentType.Should().Be(ReportFormat.Xlsx.ContentType);

        using var workbook = new XLWorkbook(new MemoryStream(store.GetContent("orders.xlsx")));
        workbook.Worksheet(1).LastRowUsed()!.RowNumber().Should().Be(2_001);
    }

    [Fact]
    public async Task MoreRowsThanMaxRows_FailsAndStoresNothing()
    {
        var store = new InMemoryFileStorage("reports");
        SpreadsheetReportExporter<TestRow> exporter = Exporter(o => o.MaxRows = 10, InMemoryStorage.CreateFactory(store));

        Result<ReportExportOutcome> result = await exporter.ExportAsync(
            Many(11),
            ReportDefinition.For<TestRow>().Column("Id", r => r.Id).Build(),
            new ReportDestination { Store = "reports", Key = "big.xlsx" });

        result.Error.Code.Should().Be(ReportingErrorCodes.RowLimitExceeded);
        store.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task ConstantMemory_AllocationsPerRowDoNotGrowWithTheRowCount()
    {
        SpreadsheetReportExporter<TestRow> exporter = Exporter();
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>()
            .Column("Id", r => r.Id)
            .Column("Name", r => r.Name)
            .Column("Amount", r => r.Amount, format: "N2")
            .Column("When", r => r.When)
            .Build();
        await exporter.ExportToStreamAsync(Many(1_000), definition, Stream.Null);

        double small = await BytesPerRowAsync(20_000);
        double large = await BytesPerRowAsync(200_000);

        large.Should().BeLessThan(small * 2, "the workbook is streamed, not built in memory");

        async Task<double> BytesPerRowAsync(int count)
        {
            GC.Collect();
            long before = GC.GetTotalAllocatedBytes(precise: true);
            (await exporter.ExportToStreamAsync(Many(count), definition, Stream.Null)).IsSuccess.Should().BeTrue();
            return (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)count;
        }
    }

    [Fact]
    public async Task EnumeratesTheRowsOnce()
    {
        using var stream = new MemoryStream();
        var rows = new SingleEnumerationAsyncEnumerable<TestRow>([new TestRow(1, "a", 0, When), new TestRow(2, "b", 0, When)]);

        Result<ReportStreamOutcome> result = await Exporter().ExportToStreamAsync(
            rows,
            ReportDefinition.For<TestRow>().Column("Id", r => r.Id).Build(),
            stream);

        result.Value.RowCount.Should().Be(2);
    }

    [Theory]
    [InlineData("N0", "#,##0")]
    [InlineData("N2", "#,##0.00")]
    [InlineData("F3", "0.000")]
    [InlineData("P1", "0.0%")]
    [InlineData("D5", "00000")]
    [InlineData("E2", "0.00E+00")]
    [InlineData("#,##0.00;(#,##0.00)", "#,##0.00;(#,##0.00)")]
    [InlineData("G", null)]
    public void NumberFormats_AreTranslated(string format, string? expected)
    {
        ExcelFormats.Number(format, CultureInfo.InvariantCulture).Should().Be(expected);
    }

    [Fact]
    public void CurrencyFormat_UsesTheCultureSymbolAndPosition()
    {
        ExcelFormats.Number("C2", CultureInfo.GetCultureInfo("en-US")).Should().Be("\"$\"#,##0.00");
        ExcelFormats.Number("C0", CultureInfo.GetCultureInfo("de-DE")).Should().Be("#,##0 \"€\"");
    }

    [Theory]
    [InlineData("yyyy-MM-dd HH:mm:ss", "yyyy-mm-dd hh:mm:ss")]
    [InlineData("dd.MM.yyyy", "dd.mm.yyyy")]
    [InlineData("h:mm tt", "h:mm AM/PM")]
    [InlineData("HH:mm:ss.fff", "hh:mm:ss.000")]
    [InlineData("'Week of' yyyy-MM-dd", "\"Week of\" yyyy-mm-dd")]
    public void DatePatterns_AreTranslated(string pattern, string expected)
    {
        ExcelFormats.TranslateDatePattern(pattern).Should().Be(expected);
    }

    [Fact]
    public void StandardDateFormats_UseTheCulturePatterns()
    {
        ExcelFormats.DateAndTime("d", CultureInfo.InvariantCulture, ExcelFormats.Date).Should().Be("mm/dd/yyyy");
        ExcelFormats.DateAndTime("G", CultureInfo.InvariantCulture, ExcelFormats.Date).Should().Be("mm/dd/yyyy hh:mm:ss");
        ExcelFormats.DateAndTime(null, CultureInfo.InvariantCulture, ExcelFormats.Date).Should().Be(ExcelFormats.Date);
    }

    [Fact]
    public async Task AddSpreadsheet_RegistersTheExporterForEveryRowType_AndValidatesOptionsAtStartup()
    {
        using (IHost host = Host(new Dictionary<string, string?>()))
        {
            await host.StartAsync();
            host.Services.GetRequiredService<ISpreadsheetReportExporter<TestRow>>().Format.Should().Be(ReportFormat.Xlsx);
            host.Services.GetRequiredService<IReportExporterFactory>().GetExporter<string>(ReportFormat.Xlsx).Should().NotBeNull();
        }

        using IHost invalid = Host(new Dictionary<string, string?> { ["SharedKernel:Reporting:Spreadsheet:MaxRows"] = "2000000" });
        var start = () => invalid.StartAsync();
        await start.Should().ThrowAsync<OptionsValidationException>().WithMessage("*MaxRows*");
    }

    private static IHost Host(Dictionary<string, string?> settings)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSharedKernelReporting().AddSpreadsheet(builder.Configuration);
        return builder.Build();
    }

    private static SpreadsheetReportExporter<TestRow> Exporter(Action<SpreadsheetExportOptions>? configure = null, IFileStorageFactory? storageFactory = null)
    {
        var options = new SpreadsheetExportOptions();
        configure?.Invoke(options);
        return new SpreadsheetReportExporter<TestRow>(new ReportingDependencies(storageFactory: storageFactory), Options.Create(options));
    }

    private static async Task<XLWorkbook> ExportAsync(TestRow[] rows, ReportDefinition<TestRow> definition)
    {
        var stream = new MemoryStream();
        Result<ReportStreamOutcome> result = await Exporter().ExportToStreamAsync(new SingleEnumerationAsyncEnumerable<TestRow>(rows), definition, stream);
        result.IsSuccess.Should().BeTrue();
        stream.Position = 0;
        return new XLWorkbook(stream);
    }

    private static async IAsyncEnumerable<TestRow> Many(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new TestRow(i, "Row" + i.ToString(CultureInfo.InvariantCulture), i * 1.5m, When.AddMinutes(i));
            if (i % 10_000 == 0)
            {
                await Task.Yield();
            }
        }
    }
}
