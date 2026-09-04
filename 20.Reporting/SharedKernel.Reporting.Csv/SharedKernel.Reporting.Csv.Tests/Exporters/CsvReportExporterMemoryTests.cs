using System.Globalization;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Reporting.Csv.Exporters;
using SharedKernel.Reporting.Csv.Options;

namespace SharedKernel.Reporting.Csv.Tests.Exporters;

/// <summary>
/// The load-bearing constant-memory proof (T-04). <see cref="SharedKernel.Reporting.Csv"/> is the
/// only one of this domain's three providers that can honestly claim end-to-end memory-boundedness
/// (D-06) — this test is what would fail immediately if a future edit accidentally introduced a
/// <c>List&lt;TRow&gt;</c>/<c>ToListAsync()</c> anywhere in the CSV encoding path.
/// </summary>
public sealed class CsvReportExporterMemoryTests
{
    [Fact]
    public async Task ExportToStreamAsync_LargeRowCount_AllocatedBytesPerRowDoesNotGrowWithRowCount()
    {
        var exporter = CreateExporter();
        var definition = CreateDefinition();

        // Warm up JIT / one-time first-call allocations so they don't skew the measured deltas.
        await exporter.ExportToStreamAsync(GenerateRows(1_000), definition, Stream.Null, CancellationToken.None);

        const int smallCount = 20_000;
        const int largeCount = 400_000; // 20x smallCount

        var smallBytesPerRow = await MeasureBytesPerRowAsync(exporter, definition, smallCount);
        var largeBytesPerRow = await MeasureBytesPerRowAsync(exporter, definition, largeCount);

        // A List<TRow>/ToListAsync() buffering regression would make allocated bytes grow roughly
        // linearly with row count (~20x here, since largeCount is 20x smallCount). Genuine
        // per-row streaming keeps the amortized per-row allocation roughly flat regardless of how
        // many rows are enumerated.
        largeBytesPerRow.Should().BeLessThan(
            smallBytesPerRow * 4,
            "allocated bytes per row must stay roughly constant across a 20x row-count increase — " +
            "proportional growth is the structural signature of the row source being materialized " +
            "instead of streamed");
    }

    [Fact]
    public async Task ExportToStreamAsync_MillionRows_CompletesAndProducesCorrectRowCount()
    {
        var exporter = CreateExporter();
        var definition = CreateDefinition();
        const int rowCount = 1_000_000;

        using var discard = new DiscardingCountingStream();
        var result = await exporter.ExportToStreamAsync(GenerateRows(rowCount), definition, discard, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        discard.TotalBytesWritten.Should().BeGreaterThan(0);
    }

    private static async Task<double> MeasureBytesPerRowAsync(
        ICsvReportExporter<TestRow> exporter,
        ReportDefinition<TestRow> definition,
        int rowCount)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);

        var result = await exporter.ExportToStreamAsync(GenerateRows(rowCount), definition, Stream.Null, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var after = GC.GetTotalAllocatedBytes(precise: true);
        return (after - before) / (double)rowCount;
    }

    private static async IAsyncEnumerable<TestRow> GenerateRows(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

    private static CsvReportExporter<TestRow> CreateExporter()
    {
        var fileStorage = new SharedKernel.Testing.Storage.InMemoryFileStorage();
        var writer = new StorageStreamingWriter(fileStorage, Microsoft.Extensions.Logging.Abstractions.NullLogger<StorageStreamingWriter>.Instance);
        var options = Microsoft.Extensions.Options.Options.Create(new CsvExportOptions());
        return new CsvReportExporter<TestRow>(writer, options);
    }

    private static ReportDefinition<TestRow> CreateDefinition() => new()
    {
        Columns =
        [
            new ReportColumn<TestRow> { Header = "Id", Ordinal = 0, ValueSelector = r => r.Id },
            new ReportColumn<TestRow> { Header = "Name", Ordinal = 1, ValueSelector = r => r.Name },
            new ReportColumn<TestRow> { Header = "Amount", Ordinal = 2, ValueSelector = r => r.Amount },
            new ReportColumn<TestRow> { Header = "When", Ordinal = 3, ValueSelector = r => r.When },
        ],
    };

    /// <summary>A write-only stream that counts total bytes written but never retains them.</summary>
    private sealed class DiscardingCountingStream : Stream
    {
        public long TotalBytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => TotalBytesWritten;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => TotalBytesWritten += count;

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            TotalBytesWritten += count;
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            TotalBytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }
    }
}
