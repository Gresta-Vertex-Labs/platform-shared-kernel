using System.Reflection;
using FluentAssertions;
using NSubstitute;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Abstractions.Tests;

/// <summary>
/// The exception that faults the upload pipe when a writer fails: it carries the writer's own <see cref="Error"/>, so
/// a storage span or an edge that sees it classifies the real cause.
/// </summary>
public sealed class ReportAbortedExceptionTests
{
    private static readonly Type AbortedType = typeof(ReportingDependencies).Assembly.GetType("SharedKernel.Reporting.ReportAbortedException")
        ?? typeof(ReportingDependencies).Assembly.GetTypes().Single(t => t.Name == "ReportAbortedException");

    [Fact]
    public void IsASealedSharedKernelException_ThatCarriesTheWritersErrorUnchanged()
    {
        Error error = ReportingErrors.RowLimitExceeded(ReportFormat.Pdf, 10_000);

        var exception = (SharedKernelException)Create(error);

        AbortedType.IsSealed.Should().BeTrue();
        exception.Error.Should().BeSameAs(error);
        exception.Error.Code.Should().Be(ReportingErrorCodes.RowLimitExceeded);
        exception.Error.Type.Should().Be(ErrorType.Validation);
        exception.Message.Should().Be(error.Message);
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void TheTwoArgumentConstructor_KeepsTheInnerException()
    {
        Error error = ReportingErrors.ConverterUnavailable();
        var inner = new IOException("boom");

        var exception = (SharedKernelException)Activator.CreateInstance(AbortedType, error, inner)!;

        exception.Error.Should().BeSameAs(error);
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ThereIsNoStringConstructor()
    {
        ConstructorInfo[] constructors = AbortedType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        constructors.Should().HaveCount(2);
        constructors.Should().OnlyContain(c => c.GetParameters().Length > 0 && c.GetParameters()[0].ParameterType == typeof(Error));
        constructors.SelectMany(c => c.GetParameters()).Should().NotContain(p => p.ParameterType == typeof(string));
    }

    [Theory]
    [InlineData(ReportingErrorCodes.RowLimitExceeded, ErrorType.Validation)]
    [InlineData(ReportingErrorCodes.ConverterUnavailable, ErrorType.Unavailable)]
    [InlineData(ReportingErrorCodes.ConversionTimeout, ErrorType.Timeout)]
    public void TheErrorTypeAnEdgeSwitchesOn_IsTheRealCauses(string code, ErrorType type)
    {
        Error error = code switch
        {
            ReportingErrorCodes.RowLimitExceeded => ReportingErrors.RowLimitExceeded(ReportFormat.Pdf, 5),
            ReportingErrorCodes.ConverterUnavailable => ReportingErrors.ConverterUnavailable(),
            _ => ReportingErrors.ConversionTimeout(TimeSpan.FromSeconds(30)),
        };

        var exception = Create(error);

        // Exactly what the three edge mappers test: a SharedKernelException, then its Error.Type.
        (exception is SharedKernelException { Error: { } carried } && carried.Type == type && carried.Code == code).Should().BeTrue();
    }

    [Fact]
    public async Task WhenTheWriterFailsMidStream_TheStoreSeesTheWritersError_NothingIsStored_AndTheErrorIsReturned()
    {
        var recording = new RecordingStore();
        var exporter = new LineExporter<Row>(Dependencies(recording)) { FailAfterRows = 2 };

        Result<ReportExportOutcome> result = await exporter.ExportAsync(
            Rows.Generate(5000), Rows.Definition, new ReportDestination { Store = "reports", Key = "out.txt" });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ReportingErrorCodes.RowLimitExceeded, "the writer's Error is returned, not a storage error");
        result.Error.Type.Should().Be(ErrorType.Validation);
        recording.Stored.Should().BeFalse("a half-written report is never stored");
        recording.Seen.Should().BeAssignableTo<SharedKernelException>().Which.Error.Code.Should().Be(ReportingErrorCodes.RowLimitExceeded);
        recording.Seen!.GetType().Name.Should().Be("ReportAbortedException");
    }

    [Fact]
    public async Task WhenTheConverterFails_TheStoreSeesTheConvertersError_NothingIsStored_AndTheErrorIsReturned()
    {
        var recording = new RecordingStore();
        var converter = new FailingConverter(Dependencies(recording));

        Result<PdfDocumentOutcome> result = await converter.ConvertAsync(
            "<p>hi</p>", new ReportDestination { Store = "reports", Key = "out.pdf" });

        result.Error.Code.Should().Be(ReportingErrorCodes.ConverterUnavailable);
        result.Error.Type.Should().Be(ErrorType.Unavailable);
        recording.Stored.Should().BeFalse();
        recording.Seen.Should().BeAssignableTo<SharedKernelException>().Which.Error.Code.Should().Be(ReportingErrorCodes.ConverterUnavailable);
    }

    private static object Create(Error error) => Activator.CreateInstance(AbortedType, error)!;

    private static ReportingDependencies Dependencies(RecordingStore store)
    {
        IFileStorageFactory factory = InMemoryStorage.CreateFactory(b => b.AddStore(new FileStoreRegistration(
            "reports", tenantScoped: false, _ => store.Substitute, (_, _) => Task.FromResult(Result.Success()))));
        return new ReportingDependencies(storageFactory: factory);
    }

    /// <summary>A store whose upload reads the body to the end and records the exception the read raised.</summary>
    private sealed class RecordingStore
    {
        public RecordingStore()
        {
            Substitute = NSubstitute.Substitute.For<IFileStorage>();
            Substitute.StoreName.Returns("reports");
            Substitute.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<FileUploadOptions?>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    var stream = call.Arg<Stream>();
                    var buffer = new byte[4096];
                    try
                    {
                        while (await stream.ReadAsync(buffer, call.Arg<CancellationToken>()) > 0)
                        {
                        }

                        Stored = true;
                        return Result<FileReference>.Success(new FileReference { Store = "reports", Key = call.Arg<string>() });
                    }
                    catch (Exception exception)
                    {
                        Seen = exception;
                        return Result<FileReference>.Failure(StorageErrors.Unavailable("reports", "upload"));
                    }
                });
        }

        public IFileStorage Substitute { get; }

        public bool Stored { get; private set; }

        public Exception? Seen { get; private set; }
    }

    private sealed class FailingConverter(ReportingDependencies dependencies) : HtmlToPdfConverterBase(dependencies)
    {
        protected override async Task<Result> RenderAsync(string html, HtmlToPdfOptions options, Stream destination, CancellationToken cancellationToken)
        {
            await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            return ReportingErrors.ConverterUnavailable();
        }
    }
}
