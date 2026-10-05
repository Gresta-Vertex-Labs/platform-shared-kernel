using System.Diagnostics;
using System.Text;
using FluentAssertions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;
using NSubstitute;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Abstractions.Tests;

public sealed class ExporterBaseTests
{
    private static readonly TenantId Acme = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));

    private readonly InMemoryFileStorage _reports = new("reports");

    [Fact]
    public async Task ExportAsync_StoresTheReport_WithRowCountSizeAndContentType()
    {
        var exporter = Exporter();

        Result<ReportExportOutcome> result = await exporter.ExportAsync(Rows.Generate(3), Rows.Definition, Destination());

        result.IsSuccess.Should().BeTrue();
        result.Value.RowCount.Should().Be(3);
        result.Value.Format.Should().Be(LineExporter<Row>.Lines);
        result.Value.StoredFile.Store.Should().Be("reports");
        result.Value.StoredFile.Key.Should().Be("out.txt");

        byte[] content = _reports.GetContent("out.txt");
        Encoding.UTF8.GetString(content).Should().Be("1;name-1\n2;name-2\n3;name-3\n");
        result.Value.SizeBytes.Should().Be(content.Length);

        Result<FileProperties> properties = await _reports.GetPropertiesAsync("out.txt");
        properties.Value.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task ExportAsync_WritesDownloadFileNameAsContentDisposition_WithAnAsciiFallback()
    {
        Result<ReportExportOutcome> result = await Exporter().ExportAsync(
            Rows.Generate(1),
            Rows.Definition,
            Destination() with { DownloadFileName = "Sipariş raporu.txt" });

        result.IsSuccess.Should().BeTrue();
        Result<FileProperties> properties = await _reports.GetPropertiesAsync("out.txt");
        properties.Value.ContentDisposition.Should().Be(
            "attachment; filename=\"Sipari_ raporu.txt\"; filename*=UTF-8''Sipari%C5%9F%20raporu.txt");
    }

    [Fact]
    public async Task ExportAsync_WithPresignExpiry_ReturnsADownloadUrl()
    {
        Result<ReportExportOutcome> result = await Exporter().ExportAsync(
            Rows.Generate(1),
            Rows.Definition,
            Destination() with { PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(5) });

        result.Value.DownloadUrl.Should().NotBeNull();
        _reports.IssuedDownloadUrls.Should().ContainSingle(u => u.Key == "out.txt");
    }

    [Fact]
    public async Task ExportAsync_WithIfNotExists_DoesNotOverwriteAnExistingReport()
    {
        var exporter = Exporter();
        ReportDestination createOnly = Destination() with { Condition = WriteCondition.IfNotExists };
        (await exporter.ExportAsync(Rows.Generate(1), Rows.Definition, createOnly)).IsSuccess.Should().BeTrue();

        Result<ReportExportOutcome> second = await exporter.ExportAsync(Rows.Generate(2), Rows.Definition, createOnly);

        second.Error.Code.Should().Be(StorageErrorCodes.AlreadyExists);
        Encoding.UTF8.GetString(_reports.GetContent("out.txt")).Should().Be("1;name-1\n");
    }

    [Fact]
    public async Task ExportAsync_WhenTheExporterReturnsAFailure_StoresNothing()
    {
        var exporter = new LineExporter<Row>(Dependencies()) { FailAfterRows = 2 };

        Result<ReportExportOutcome> result = await exporter.ExportAsync(Rows.Generate(5000), Rows.Definition, Destination());

        result.Error.Code.Should().Be(ReportingErrorCodes.RowLimitExceeded);
        _reports.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_WhenTheExporterThrows_RethrowsItsException_AndStoresNothing()
    {
        var exporter = new LineExporter<Row>(Dependencies()) { ThrowAtRow = 2 };

        var export = () => exporter.ExportAsync(Rows.Generate(5000), Rows.Definition, Destination());

        await export.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        _reports.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_WhenTheUploadFailsEarly_StopsReadingTheRows()
    {
        // A store that rejects the upload without reading the body, as S3 does for a failed precondition.
        var rejecting = Substitute.For<IFileStorage>();
        rejecting.StoreName.Returns("reports");
        rejecting.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<FileUploadOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Result<FileReference>.Failure(StorageErrors.Unavailable("reports", "upload")));
        IFileStorageFactory factory = InMemoryStorage.CreateFactory(b => b.AddStore(new FileStoreRegistration(
            "reports", tenantScoped: false, _ => rejecting, (_, _) => Task.FromResult(Result.Success()))));
        var produced = 0;

        Result<ReportExportOutcome> result = await new LineExporter<Row>(new ReportingDependencies(storageFactory: factory))
            .ExportAsync(Counted(), Rows.Definition, Destination());

        result.Error.Code.Should().Be(StorageErrorCodes.Unavailable);
        produced.Should().BeLessThan(100_000, "a rejected upload must not drain the whole row source");

        async IAsyncEnumerable<Row> Counted()
        {
            await foreach (Row row in Rows.Generate(100_000))
            {
                produced++;
                yield return row;
            }
        }
    }

    [Fact]
    public async Task ExportAsync_Cancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var export = () => Exporter().ExportAsync(Rows.Generate(10), Rows.Definition, Destination(), cancellation.Token);

        await export.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExportAsync_InvalidInput_ReturnsAFailureBeforeTouchingStorage()
    {
        Result<ReportExportOutcome> result = await Exporter().ExportAsync(
            Rows.Generate(1),
            new ReportDefinition<Row> { Columns = [] },
            Destination());

        result.Error.Code.Should().Be(ReportingErrorCodes.InvalidDefinition);
        _reports.UploadedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_ToATenantStore_WritesUnderTheTenant()
    {
        var tenantStore = new InMemoryFileStorage("tenant-reports");
        IFileStorageFactory factory = InMemoryStorage.CreateFactory(b => b.AddInMemoryTenantStore(tenantStore));
        var exporter = new LineExporter<Row>(new ReportingDependencies(storageFactory: factory));

        Result<ReportExportOutcome> result = await exporter.ExportAsync(
            Rows.Generate(1),
            Rows.Definition,
            new ReportDestination { Store = "tenant-reports", TenantId = Acme, Key = "out.txt" });

        result.Value.StoredFile.TenantId.Should().Be(Acme);
        tenantStore.Keys.Should().ContainSingle().Which.Should().Be(InMemoryFileStorage.TenantKey(Acme, "out.txt"));
    }

    [Fact]
    public async Task ExportAsync_WithoutStorage_ThrowsAnExplainingException()
    {
        var exporter = new LineExporter<Row>(new ReportingDependencies());

        var export = () => exporter.ExportAsync(Rows.Generate(1), Rows.Definition, Destination());

        await export.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddSharedKernelStorage*");
    }

    [Fact]
    public async Task ExportToStreamAsync_WritesToTheStream_AndLeavesItOpen()
    {
        using var stream = new MemoryStream();

        Result<ReportStreamOutcome> result = await Exporter().ExportToStreamAsync(Rows.Generate(2), Rows.Definition, stream);

        result.Value.Should().Be(new ReportStreamOutcome { Format = LineExporter<Row>.Lines, RowCount = 2, SizeBytes = stream.Length });
        stream.CanWrite.Should().BeTrue();
    }

    [Fact]
    public async Task Export_EmitsASpanWithFormatRowsAndSize_AndMarksFailures()
    {
        using var testSource = new ActivitySource(nameof(Export_EmitsASpanWithFormatRowsAndSize_AndMarksFailures));
        var spans = new List<Activity>();
        ActivityTraceId traceId = default;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "SharedKernel.Reporting" || source == testSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = span =>
            {
                if (span.TraceId == traceId && span.Source.Name == "SharedKernel.Reporting")
                {
                    lock (spans)
                    {
                        spans.Add(span);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        using Activity test = testSource.StartActivity("test")!;
        traceId = test.TraceId;

        await Exporter().ExportAsync(Rows.Generate(2), Rows.Definition, Destination());
        await new LineExporter<Row>(Dependencies()) { FailAfterRows = 0 }.ExportAsync(Rows.Generate(2), Rows.Definition, Destination() with { Key = "b.txt" });

        spans.Should().HaveCount(2);
        spans[0].GetTagItem("reporting.format").Should().Be("lines");
        spans[0].GetTagItem("reporting.row_count").Should().Be(2L);
        spans[0].GetTagItem("reporting.store").Should().Be("reports");
        spans[0].Status.Should().Be(ActivityStatusCode.Unset);
        spans[1].Status.Should().Be(ActivityStatusCode.Error);
        spans[1].GetTagItem("error.type").Should().Be(ReportingErrorCodes.RowLimitExceeded);
        spans.Should().OnlyContain(s => s.Tags.All(t => !t.Value!.Contains("name-", StringComparison.Ordinal)));
    }

    private static ReportDestination Destination() => new() { Store = "reports", Key = "out.txt" };

    private ReportingDependencies Dependencies() => new(storageFactory: InMemoryStorage.CreateFactory(_reports));

    private LineExporter<Row> Exporter() => new(Dependencies());
}
