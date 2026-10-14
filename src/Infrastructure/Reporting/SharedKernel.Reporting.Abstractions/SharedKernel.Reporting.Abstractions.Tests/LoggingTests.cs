using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Reporting.Abstractions.Tests;

/// <summary>The log records of the reporting pipeline: ids, templates, levels, the exception parameter and hygiene.</summary>
public sealed class LoggingTests
{
    private readonly InMemoryLogger<ReportingDependencies> _logger = new();

    [Fact]
    public async Task Export_Success_LogsStartedAndCompleted_AtDebug()
    {
        using var stream = new MemoryStream();

        await Exporter().ExportToStreamAsync(Rows.Generate(2), Rows.Definition, stream);

        IReadOnlyList<LogRecord> records = _logger.Records;
        records.Should().HaveCount(2);
        records.ShouldHaveLogged(new EventId(20000), LogLevel.Debug)
            .Template.Should().Be("Report {Operation} for format '{Format}' started.");
        records.ShouldHaveLogged(new EventId(20001), LogLevel.Debug)
            .Template.Should().Be("Report {Operation} for format '{Format}' produced {RowCount} rows and {SizeBytes} bytes in {ElapsedMilliseconds} ms.");
        records.ShouldHaveLoggedWithProperty(new EventId(20001), "RowCount", 2L);
        records.ShouldFollowLogConvention();
    }

    [Fact]
    public async Task Export_ReturningAFailure_LogsAWarningWithCodeAndType()
    {
        using var stream = new MemoryStream();

        await Exporter(failAfterRows: 0).ExportToStreamAsync(Rows.Generate(2), Rows.Definition, stream);

        IReadOnlyList<LogRecord> records = _logger.Records;
        records.ShouldHaveLogged(new EventId(20002), LogLevel.Warning)
            .Template.Should().Be("Report {Operation} for format '{Format}' failed with error code '{ErrorCode}' of type '{ErrorType}'.");
        records.ShouldHaveLoggedWithProperty(new EventId(20002), "ErrorCode", ReportingErrorCodes.RowLimitExceeded);
        records.ShouldHaveLoggedWithProperty(new EventId(20002), "ErrorType", "Validation");
        records.ShouldHaveLoggedWithoutException(new EventId(20002));
        records.ShouldFollowLogConvention();
    }

    [Fact]
    public async Task Export_Throwing_LogsAnErrorWithTheException()
    {
        using var stream = new MemoryStream();
        LineExporter<Row> exporter = Exporter(throwAtRow: 0);

        Func<Task> export = () => exporter.ExportToStreamAsync(Rows.Generate(2), Rows.Definition, stream);

        await export.Should().ThrowAsync<InvalidOperationException>();
        IReadOnlyList<LogRecord> records = _logger.Records;
        records.ShouldHaveLogged(new EventId(20003), LogLevel.Error)
            .Template.Should().Be("Report {Operation} for format '{Format}' threw an exception.");
        records.ShouldHaveLoggedWithException(new EventId(20003), typeof(InvalidOperationException));
        records.ShouldFollowLogConvention();
    }

    [Fact]
    public async Task Export_Cancelled_LogsAtDebug()
    {
        using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> export = () => Exporter().ExportToStreamAsync(Rows.Generate(2), Rows.Definition, stream, cancellation.Token);

        await export.Should().ThrowAsync<OperationCanceledException>();
        _logger.Records.ShouldHaveLogged(new EventId(20004), LogLevel.Debug)
            .Template.Should().Be("Report {Operation} for format '{Format}' was cancelled.");
    }

    [Fact]
    public async Task Convert_Success_LogsConvertedAtDebug()
    {
        var converter = new TextConverter(new ReportingDependencies(_logger));
        using var stream = new MemoryStream();

        await converter.ConvertToStreamAsync("<p>x</p>", stream);

        _logger.Records.ShouldHaveLogged(new EventId(20006), LogLevel.Debug)
            .Template.Should().Be("PDF conversion produced {SizeBytes} bytes in {ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task Export_FailingToCreateTheDownloadLink_LogsAWarningWithTheStoreAndCode()
    {
        var exporter = new LineExporter<Row>(new ReportingDependencies(_logger, StoreSupport.PresignFailing()));
        var destination = new ReportDestination { Store = "reports", Key = "out.txt", PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(5) };

        Result<ReportExportOutcome> result = await exporter.ExportAsync(Rows.Generate(1), Rows.Definition, destination);

        result.IsFailure.Should().BeTrue();
        _logger.Records.ShouldHaveLogged(new EventId(20005), LogLevel.Warning)
            .Template.Should().Be("The report was stored in store '{Store}' but its download link could not be created, error code '{ErrorCode}'.");
        _logger.Records.ShouldHaveLoggedWithProperty(new EventId(20005), "Store", "reports");
    }

    [Fact]
    public async Task EveryRecord_IsInTheAbstractionsSubBlock_AndLeaksNoRowKeyOrFileName()
    {
        using var stream = new MemoryStream();
        var exporter = new LineExporter<Row>(new ReportingDependencies(_logger, StoreSupport.Factory(failUpload: false)));

        await exporter.ExportAsync(
            Rows.Generate(2),
            Rows.Definition,
            new ReportDestination { Store = "reports", Key = "secret-object-key.txt", DownloadFileName = "secret-file-name.txt" });
        await Exporter(failAfterRows: 0).ExportToStreamAsync(Rows.Generate(2), Rows.Definition, stream);

        _logger.Records.Should().NotBeEmpty();
        _logger.Records
            .ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Reporting, 0)
            .ShouldNotLeak("name-1", "name-2", "secret-object-key", "secret-file-name")
            .ShouldFollowLogConvention();
    }

    [Fact]
    public void TheLoggerIsRequired()
    {
        Func<ReportingDependencies> create = () => new ReportingDependencies(null!);

        create.Should().Throw<ArgumentNullException>();
    }

    private LineExporter<Row> Exporter(long? failAfterRows = null, long? throwAtRow = null) =>
        new(new ReportingDependencies(_logger)) { FailAfterRows = failAfterRows, ThrowAtRow = throwAtRow };
}
