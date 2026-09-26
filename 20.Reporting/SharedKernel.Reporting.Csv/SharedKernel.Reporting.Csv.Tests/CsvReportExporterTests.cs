using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Csv.Tests;

public sealed class CsvReportExporterTests
{
    private static readonly ReportDefinition<TestRow> ValueOnly = ReportDefinition.For<TestRow>().Column("Value", r => r.Name).Build();

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("has\rcarriage", "\"has\rcarriage\"")]
    [InlineData("has\nnewline", "\"has\nnewline\"")]
    [InlineData("", "")]
    public async Task EscapesFieldsPerRfc4180(string value, string expected)
    {
        string csv = await ExportAsync([Row(name: value)], ValueOnly);

        csv.Should().Be($"Value\r\n{expected}\r\n");
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+1+2", "'+1+2")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("safe=text", "safe=text")]
    public async Task EscapesFormulaInjection_InText(string value, string expected)
    {
        string csv = await ExportAsync([Row(name: value)], ValueOnly);

        csv.Should().Be($"Value\r\n{expected}\r\n");
    }

    [Fact]
    public async Task NeverPrefixesANegativeNumber_AndCanTurnEscapingOff()
    {
        ReportDefinition<TestRow> amount = ReportDefinition.For<TestRow>().Column("Amount", r => r.Amount).Build();

        (await ExportAsync([Row(amount: -5.25m)], amount)).Should().Be("Amount\r\n-5.25\r\n");
        (await ExportAsync([Row(name: "=1+1")], ValueOnly, o => o.EscapeFormulas = false)).Should().Be("Value\r\n=1+1\r\n");
    }

    [Fact]
    public async Task EscapesAFormulaInAHeader()
    {
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>().Column("=evil", r => r.Id).Build();

        (await ExportAsync([], definition)).Should().Be("'=evil\r\n");
    }

    [Fact]
    public async Task DeDeCulture_WithSemicolonDelimiter_KeepsTheDecimalCommaUnquoted()
    {
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>()
            .Culture(CultureInfo.GetCultureInfo("de-DE"))
            .Column("Id", r => r.Id)
            .Column("Amount", r => r.Amount, format: "N2")
            .Build();

        string csv = await ExportAsync([Row(id: 7, amount: 1234.5m)], definition, o => o.Delimiter = ';');

        csv.Should().Be("Id;Amount\r\n7;1.234,50\r\n");
    }

    [Fact]
    public async Task DeDeCulture_WithCommaDelimiter_QuotesTheDecimalComma()
    {
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>()
            .Culture(CultureInfo.GetCultureInfo("de-DE"))
            .Column("Amount", r => r.Amount)
            .Build();

        (await ExportAsync([Row(amount: 1234.5m)], definition)).Should().Be("Amount\r\n\"1234,5\"\r\n");
    }

    [Fact]
    public async Task NullIsAnEmptyField_AndColumnsKeepTheirOrder()
    {
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>()
            .Column("B", r => (string?)null)
            .Column("A", r => r.Id)
            .Build();

        (await ExportAsync([Row(id: 3)], definition)).Should().Be("B,A\r\n,3\r\n");
    }

    [Fact]
    public async Task WritesABomByDefault_AndCanOmitTheHeader()
    {
        using var stream = new MemoryStream();
        await Exporter(o => o.IncludeUtf8Bom = true).ExportToStreamAsync(Generate([Row()]), ValueOnly, stream);
        stream.ToArray().Take(3).Should().Equal(0xEF, 0xBB, 0xBF);

        (await ExportAsync([Row(name: "x")], ValueOnly, o => o.IncludeHeaderRow = false)).Should().Be("x\r\n");
    }

    [Fact]
    public async Task ExportAsync_StoresCsvWithItsContentType()
    {
        var store = new InMemoryFileStorage("reports");
        var exporter = Exporter(storageFactory: InMemoryStorage.CreateFactory(store));

        Result<ReportExportOutcome> result = await exporter.ExportAsync(
            Generate([Row(), Row()]),
            ValueOnly,
            new ReportDestination { Store = "reports", Key = "a.csv" });

        result.Value.RowCount.Should().Be(2);
        result.Value.Format.Should().Be(ReportFormat.Csv);
        result.Value.SizeBytes.Should().Be(store.GetContent("a.csv").Length);
        (await store.GetPropertiesAsync("a.csv")).Value.ContentType.Should().Be("text/csv");
    }

    [Fact]
    public async Task ConstantMemory_AllocationsPerRowDoNotGrowWithTheRowCount()
    {
        var exporter = Exporter();
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>()
            .Column("Id", r => r.Id)
            .Column("Name", r => r.Name)
            .Column("Amount", r => r.Amount)
            .Column("When", r => r.When)
            .Build();
        await exporter.ExportToStreamAsync(Many(1_000), definition, Stream.Null);

        double small = await BytesPerRowAsync(20_000);
        double large = await BytesPerRowAsync(400_000);

        large.Should().BeLessThan(small * 2, "a streamed export allocates the same per row whatever the row count");

        async Task<double> BytesPerRowAsync(int count)
        {
            GC.Collect();
            long before = GC.GetTotalAllocatedBytes(precise: true);
            (await exporter.ExportToStreamAsync(Many(count), definition, Stream.Null)).IsSuccess.Should().BeTrue();
            return (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)count;
        }
    }

    [Fact]
    public async Task AddCsv_RegistersTheExporterForEveryRowType()
    {
        using IHost host = Host(new Dictionary<string, string?> { ["SharedKernel:Reporting:Csv:Delimiter"] = ";" });
        await host.StartAsync();

        host.Services.GetRequiredService<ICsvReportExporter<TestRow>>().Format.Should().Be(ReportFormat.Csv);
        host.Services.GetRequiredService<ICsvReportExporter<string>>().Should().NotBeNull();
        var factory = host.Services.GetRequiredService<IReportExporterFactory>();
        factory.GetExporter<TestRow>(factory.ParseFormat("csv").Value).Should().BeAssignableTo<ICsvReportExporter<TestRow>>();
        host.Services.GetRequiredService<IOptions<CsvExportOptions>>().Value.Delimiter.Should().Be(';');
    }

    [Fact]
    public async Task AddCsv_InvalidDelimiter_FailsAtStartup()
    {
        using IHost host = Host(new Dictionary<string, string?> { ["SharedKernel:Reporting:Csv:Delimiter"] = "\"" });

        var start = () => host.StartAsync();

        await start.Should().ThrowAsync<OptionsValidationException>().WithMessage("*Delimiter*");
    }

    private static IHost Host(Dictionary<string, string?> settings)
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSharedKernelReporting().AddCsv(builder.Configuration);
        return builder.Build();
    }

    private static CsvReportExporter<TestRow> Exporter(Action<CsvExportOptions>? configure = null, IFileStorageFactory? storageFactory = null)
    {
        var options = new CsvExportOptions { IncludeUtf8Bom = false };
        configure?.Invoke(options);
        return new CsvReportExporter<TestRow>(new ReportingDependencies(storageFactory: storageFactory), Options.Create(options));
    }

    private static async Task<string> ExportAsync(TestRow[] rows, ReportDefinition<TestRow> definition, Action<CsvExportOptions>? configure = null)
    {
        using var stream = new MemoryStream();
        Result<ReportStreamOutcome> result = await Exporter(configure).ExportToStreamAsync(Generate(rows), definition, stream);
        result.IsSuccess.Should().BeTrue();
        result.Value.RowCount.Should().Be(rows.Length);
        result.Value.SizeBytes.Should().Be(stream.Length);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static TestRow Row(int id = 1, string name = "n", decimal amount = 1m) => new(id, name, amount, DateTime.UnixEpoch);

    private static async IAsyncEnumerable<TestRow> Generate(TestRow[] rows)
    {
        foreach (TestRow row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }

    private static async IAsyncEnumerable<TestRow> Many(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new TestRow(i, "Row" + i.ToString(CultureInfo.InvariantCulture), i * 1.5m, DateTime.UnixEpoch.AddMinutes(i));
            if (i % 50_000 == 0)
            {
                await Task.Yield();
            }
        }
    }
}
