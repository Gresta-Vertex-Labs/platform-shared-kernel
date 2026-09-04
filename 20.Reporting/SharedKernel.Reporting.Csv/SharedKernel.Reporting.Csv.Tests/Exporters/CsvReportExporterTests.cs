using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Reporting.Csv.Exporters;
using SharedKernel.Reporting.Csv.Options;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Csv.Tests.Exporters;

public sealed class CsvReportExporterTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("has\rcarriage", "\"has\rcarriage\"")]
    [InlineData("has\nnewline", "\"has\nnewline\"")]
    [InlineData("all,of\"it\r\n", "\"all,of\"\"it\r\n\"")]
    public async Task ExportToStreamAsync_EscapesFieldsPerRfc4180(string rawValue, string expectedEncodedField)
    {
        var exporter = CreateExporter(includeUtf8Bom: false);
        var definition = SingleColumnDefinition();

        using var destination = new MemoryStream();
        var result = await exporter.ExportToStreamAsync(
            SingleRow(rawValue),
            definition,
            destination,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var text = Encoding.UTF8.GetString(destination.ToArray());
        var expected = $"Value\r\n{expectedEncodedField}\r\n";
        text.Should().Be(expected);
    }

    [Fact]
    public async Task ExportToStreamAsync_DeDECulture_DecimalCommaIsQuotedByGeneralRule()
    {
        // No numeric-specific special case anywhere in the encoder — the same "contains the
        // delimiter -> quote it" rule that handles embedded commas in text handles a de-DE decimal
        // comma too.
        var exporter = CreateExporter(includeUtf8Bom: false);
        var definition = new ReportDefinition<TestRow>
        {
            Culture = CultureInfo.GetCultureInfo("de-DE"),
            Columns = [new ReportColumn<TestRow> { Header = "Amount", Ordinal = 0, ValueSelector = r => r.Amount }],
        };

        using var destination = new MemoryStream();
        var rows = SingleTestRow(new TestRow(1, "x", 1234.5m, DateTime.UnixEpoch));
        var result = await exporter.ExportToStreamAsync(rows, definition, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var text = Encoding.UTF8.GetString(destination.ToArray());
        text.Should().Be("Amount\r\n\"1234,5\"\r\n");
    }

    [Fact]
    public async Task ExportToStreamAsync_NullAndEmptyValues_WriteBlankNeverLiteralNull()
    {
        var exporter = CreateExporter(includeUtf8Bom: false);
        var definition = new ReportDefinition<TestRow>
        {
            Columns = [new ReportColumn<TestRow> { Header = "Name", Ordinal = 0, ValueSelector = r => r.Name }],
        };

        using var destination = new MemoryStream();
        var rows = SingleTestRow(new TestRow(1, null!, 0m, DateTime.UnixEpoch));
        var result = await exporter.ExportToStreamAsync(rows, definition, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var text = Encoding.UTF8.GetString(destination.ToArray());
        text.Should().Be("Name\r\n\r\n");
        text.Should().NotContain("null");
    }

    [Fact]
    public async Task ExportToStreamAsync_LeadingTrailingWhitespace_PreservedVerbatim()
    {
        var exporter = CreateExporter(includeUtf8Bom: false);
        var definition = SingleColumnDefinition();

        using var destination = new MemoryStream();
        var result = await exporter.ExportToStreamAsync(SingleRow("  padded  "), definition, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var text = Encoding.UTF8.GetString(destination.ToArray());
        text.Should().Be("Value\r\n  padded  \r\n");
    }

    [Fact]
    public async Task ExportToStreamAsync_ColumnOrdinal_ControlsOutputOrderRegardlessOfDeclarationOrder()
    {
        var exporter = CreateExporter(includeUtf8Bom: false);
        var definition = new ReportDefinition<TestRow>
        {
            Columns =
            [
                new ReportColumn<TestRow> { Header = "Second", Ordinal = 1, ValueSelector = r => r.Name },
                new ReportColumn<TestRow> { Header = "First", Ordinal = 0, ValueSelector = r => r.Id },
            ],
        };

        using var destination = new MemoryStream();
        var rows = SingleTestRow(new TestRow(42, "answer", 0m, DateTime.UnixEpoch));
        var result = await exporter.ExportToStreamAsync(rows, definition, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var text = Encoding.UTF8.GetString(destination.ToArray());
        text.Should().Be("First,Second\r\n42,answer\r\n");
    }

    [Fact]
    public async Task ExportToStreamAsync_IncludeUtf8Bom_True_PrependsBom()
    {
        var exporter = CreateExporter(includeUtf8Bom: true);
        var definition = SingleColumnDefinition();

        using var destination = new MemoryStream();
        await exporter.ExportToStreamAsync(SingleRow("x"), definition, destination, CancellationToken.None);

        var bytes = destination.ToArray();
        bytes.Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Fact]
    public async Task ExportToStreamAsync_IncludeUtf8Bom_False_NoBom()
    {
        var exporter = CreateExporter(includeUtf8Bom: false);
        var definition = SingleColumnDefinition();

        using var destination = new MemoryStream();
        await exporter.ExportToStreamAsync(SingleRow("x"), definition, destination, CancellationToken.None);

        var bytes = destination.ToArray();
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(bytes).Should().StartWith("Value");
    }

    [Fact]
    public async Task ExportToStreamAsync_EmptyColumns_ReturnsValidationFailure()
    {
        var exporter = CreateExporter(includeUtf8Bom: false);
        var definition = new ReportDefinition<TestRow> { Columns = [] };

        using var destination = new MemoryStream();
        var result = await exporter.ExportToStreamAsync(SingleRow("x"), definition, destination, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reporting.empty_columns");
    }

    [Fact]
    public async Task ExportToStreamAsync_Cancellation_StopsConsumingSourcePromptly()
    {
        var exporter = CreateExporter(includeUtf8Bom: false);
        var definition = SingleColumnDefinition();
        using var cts = new CancellationTokenSource();
        var rowsEnumerated = 0;

        var act = async () =>
        {
            using var destination = new MemoryStream();
            await exporter.ExportToStreamAsync(
                CancelingSource(cts, () => rowsEnumerated),
                definition,
                destination,
                cts.Token);
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        rowsEnumerated.Should().BeLessThan(1_000, "cancellation must stop enumeration promptly rather than draining the whole source");

        async IAsyncEnumerable<TestRow> CancelingSource(CancellationTokenSource source, Func<int> countAccessor, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < 1_000; i++)
            {
                rowsEnumerated = i;
                if (i == 3)
                {
                    source.Cancel();
                }

                cancellationToken.ThrowIfCancellationRequested();
                yield return new TestRow(i, "row", 0m, DateTime.UnixEpoch);
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task ExportAsync_DeliversThroughStorageAndPresignsUrlOnlyWhenRequested()
    {
        var fileStorage = new InMemoryFileStorage();
        var blobUriGenerator = new InMemoryBlobUriGenerator();
        var writer = new StorageStreamingWriter(fileStorage, NullLogger<StorageStreamingWriter>.Instance, blobUriGenerator);
        var exporter = new CsvReportExporter<TestRow>(writer, Microsoft.Extensions.Options.Options.Create(new CsvExportOptions()));
        var definition = SingleColumnDefinition();
        var destination = new ReportDestination { Bucket = "reports", Key = "export.csv" };

        var result = await exporter.ExportAsync(SingleRow("hello"), definition, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StoredFile.Bucket.Should().Be("reports");
        result.Value.StoredFile.Key.Should().Be("export.csv");
        result.Value.RowCount.Should().Be(1);
        result.Value.DownloadUrl.Should().BeNull();
        fileStorage.WasUploaded("reports", "export.csv").Should().BeTrue();

        var withPresign = destination with { Key = "export2.csv", PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(15) };
        var presignedResult = await exporter.ExportAsync(SingleRow("hello"), definition, withPresign, CancellationToken.None);

        presignedResult.IsSuccess.Should().BeTrue();
        presignedResult.Value.DownloadUrl.Should().NotBeNull();
    }

    private static ReportDefinition<TestRow> SingleColumnDefinition() => new()
    {
        Columns = [new ReportColumn<TestRow> { Header = "Value", Ordinal = 0, ValueSelector = r => r.Name }],
    };

    private static async IAsyncEnumerable<TestRow> SingleRow(string value)
    {
        await Task.Yield();
        yield return new TestRow(1, value, 0m, DateTime.UnixEpoch);
    }

    private static async IAsyncEnumerable<TestRow> SingleTestRow(TestRow row)
    {
        await Task.Yield();
        yield return row;
    }

    private static CsvReportExporter<TestRow> CreateExporter(bool includeUtf8Bom)
    {
        var fileStorage = new InMemoryFileStorage();
        var writer = new StorageStreamingWriter(fileStorage, NullLogger<StorageStreamingWriter>.Instance);
        var options = Microsoft.Extensions.Options.Options.Create(new CsvExportOptions { IncludeUtf8Bom = includeUtf8Bom });
        return new CsvReportExporter<TestRow>(writer, options);
    }
}
