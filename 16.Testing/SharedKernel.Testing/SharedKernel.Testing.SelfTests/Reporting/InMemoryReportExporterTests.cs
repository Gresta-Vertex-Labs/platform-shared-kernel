using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Reporting;

namespace SharedKernel.Testing.SelfTests.Reporting;

/// <summary>
/// Proves <see cref="InMemoryReportExporter{TRow}"/> genuinely implements
/// <see cref="SharedKernel.Reporting.Abstractions.Exporters.IReportExporter{TRow}"/> — both members
/// fully drain the supplied <see cref="IAsyncEnumerable{T}"/>, <see cref="InMemoryReportExporter{TRow}.ShouldHaveExported"/>
/// throws/returns correctly on found/not-found, <see cref="InMemoryReportExporter{TRow}.SimulateFailure"/>
/// forces a failure outcome while still capturing rows, and no real
/// <c>IFileStorage</c>/<c>.Csv</c>/<c>.Spreadsheet</c>/<c>.Pdf</c> dependency is taken anywhere. No
/// consuming service has adopted this fake yet, so this self-test is the only behavioral proof
/// today, per the SelfTests routing rule.
/// </summary>
public sealed class InMemoryReportExporterTests
{
    private sealed record TestRow(int Id, string Name);

    private static async IAsyncEnumerable<TestRow> RowsAsync(params TestRow[] rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }

    private static ReportDefinition<TestRow> CreateDefinition() =>
        new()
        {
            Columns =
            [
                new ReportColumn<TestRow> { Header = "Id", Ordinal = 0, ValueSelector = r => r.Id },
                new ReportColumn<TestRow> { Header = "Name", Ordinal = 1, ValueSelector = r => r.Name },
            ],
        };

    private static ReportDestination CreateDestination(TimeSpan? presignedExpiry = null) =>
        new() { Bucket = "reports", Key = "export.csv", PresignedDownloadUrlExpiry = presignedExpiry };

    [Fact]
    public async Task ExportAsync_DrainsAllRows_MakesThemAvailableViaLastRows()
    {
        var exporter = new InMemoryReportExporter<TestRow>();
        var rows = RowsAsync(new TestRow(1, "Alice"), new TestRow(2, "Bob"));

        var result = await exporter.ExportAsync(rows, CreateDefinition(), CreateDestination(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, exporter.LastRows.Count);
        Assert.Equal("Alice", exporter.LastRows[0].Name);
        Assert.Equal(2, result.Value.RowCount);
    }

    [Fact]
    public async Task ExportAsync_Success_RecordsDefinitionAndDestination()
    {
        var exporter = new InMemoryReportExporter<TestRow>();
        var definition = CreateDefinition();
        var destination = CreateDestination();

        await exporter.ExportAsync(RowsAsync(), definition, destination, CancellationToken.None);

        Assert.Same(definition, exporter.LastDefinition);
        Assert.Same(destination, exporter.LastDestination);
    }

    [Fact]
    public async Task ExportAsync_Success_FabricatesStoredFileFromDestination_NoRealIFileStorage()
    {
        var exporter = new InMemoryReportExporter<TestRow>();
        var destination = CreateDestination();

        var result = await exporter.ExportAsync(RowsAsync(), CreateDefinition(), destination, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(destination.Bucket, result.Value.StoredFile.Bucket);
        Assert.Equal(destination.Key, result.Value.StoredFile.Key);
        Assert.Null(result.Value.DownloadUrl);
    }

    [Fact]
    public async Task ExportAsync_PresignedUrlRequested_FabricatesDeterministicDownloadUrl()
    {
        var clock = new FakeClock();
        var exporter = new InMemoryReportExporter<TestRow>(clock);
        var expiry = TimeSpan.FromMinutes(15);
        var destination = CreateDestination(presignedExpiry: expiry);

        var result = await exporter.ExportAsync(RowsAsync(), CreateDefinition(), destination, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.DownloadUrl);
        Assert.Equal(clock.UtcNow + expiry, result.Value.DownloadUrl!.ExpiresAt);
    }

    [Fact]
    public async Task ExportAsync_SimulateFailure_ReturnsFailure_ButStillCapturesRows()
    {
        var exporter = new InMemoryReportExporter<TestRow> { SimulateFailure = true };
        var rows = RowsAsync(new TestRow(1, "Alice"));

        var result = await exporter.ExportAsync(rows, CreateDefinition(), CreateDestination(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Single(exporter.LastRows);
    }

    [Fact]
    public async Task ExportToStreamAsync_DrainsAllRows_MakesThemAvailableViaLastRows()
    {
        var exporter = new InMemoryReportExporter<TestRow>();
        using var stream = new MemoryStream();
        var rows = RowsAsync(new TestRow(1, "Alice"), new TestRow(2, "Bob"), new TestRow(3, "Carol"));

        var result = await exporter.ExportToStreamAsync(rows, CreateDefinition(), stream, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, exporter.LastRows.Count);
    }

    [Fact]
    public async Task ExportToStreamAsync_SimulateFailure_ReturnsFailure()
    {
        var exporter = new InMemoryReportExporter<TestRow> { SimulateFailure = true };
        using var stream = new MemoryStream();

        var result = await exporter.ExportToStreamAsync(RowsAsync(new TestRow(1, "Alice")), CreateDefinition(), stream, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Single(exporter.LastRows);
    }

    [Fact]
    public async Task ExportToStreamAsync_NeverSetsLastDestination()
    {
        var exporter = new InMemoryReportExporter<TestRow>();
        using var stream = new MemoryStream();

        await exporter.ExportToStreamAsync(RowsAsync(), CreateDefinition(), stream, CancellationToken.None);

        Assert.Null(exporter.LastDestination);
    }

    [Fact]
    public void ShouldHaveExported_NoExportYet_Throws()
    {
        var exporter = new InMemoryReportExporter<TestRow>();

        Assert.Throws<InvalidOperationException>(() => exporter.ShouldHaveExported());
    }

    [Fact]
    public async Task ShouldHaveExported_AfterExport_NoPredicate_Succeeds()
    {
        var exporter = new InMemoryReportExporter<TestRow>();
        await exporter.ExportAsync(RowsAsync(new TestRow(1, "Alice")), CreateDefinition(), CreateDestination(), CancellationToken.None);

        var exception = Record.Exception(() => exporter.ShouldHaveExported());

        Assert.Null(exception);
    }

    [Fact]
    public async Task ShouldHaveExported_PredicateSatisfied_Succeeds()
    {
        var exporter = new InMemoryReportExporter<TestRow>();
        await exporter.ExportAsync(RowsAsync(new TestRow(1, "Alice"), new TestRow(2, "Bob")), CreateDefinition(), CreateDestination(), CancellationToken.None);

        var exception = Record.Exception(() => exporter.ShouldHaveExported(rows => rows.Count == 2));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ShouldHaveExported_PredicateNotSatisfied_Throws()
    {
        var exporter = new InMemoryReportExporter<TestRow>();
        await exporter.ExportAsync(RowsAsync(new TestRow(1, "Alice")), CreateDefinition(), CreateDestination(), CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => exporter.ShouldHaveExported(rows => rows.Count == 99));
    }

    [Fact]
    public async Task Reset_ClearsCapturedStateAndSimulateFailure()
    {
        var exporter = new InMemoryReportExporter<TestRow> { SimulateFailure = true };
        await exporter.ExportAsync(RowsAsync(new TestRow(1, "Alice")), CreateDefinition(), CreateDestination(), CancellationToken.None);

        exporter.Reset();

        Assert.Empty(exporter.LastRows);
        Assert.Null(exporter.LastDefinition);
        Assert.Null(exporter.LastDestination);
        Assert.False(exporter.SimulateFailure);
        Assert.Throws<InvalidOperationException>(() => exporter.ShouldHaveExported());
    }

    [Fact]
    public async Task ExportAsync_EmptyRowSource_ReportsZeroRowCount()
    {
        var exporter = new InMemoryReportExporter<TestRow>();

        var result = await exporter.ExportAsync(RowsAsync(), CreateDefinition(), CreateDestination(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.RowCount);
        Assert.Empty(exporter.LastRows);
    }

    [Fact]
    public async Task SecondExport_OverwritesPreviouslyCapturedRows()
    {
        var exporter = new InMemoryReportExporter<TestRow>();
        await exporter.ExportAsync(RowsAsync(new TestRow(1, "Alice"), new TestRow(2, "Bob")), CreateDefinition(), CreateDestination(), CancellationToken.None);

        await exporter.ExportAsync(RowsAsync(new TestRow(3, "Carol")), CreateDefinition(), CreateDestination(), CancellationToken.None);

        Assert.Single(exporter.LastRows);
        Assert.Equal("Carol", exporter.LastRows[0].Name);
    }
}
