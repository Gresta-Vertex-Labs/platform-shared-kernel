using System.Diagnostics.Metrics;
using NSubstitute;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Abstractions.Tests;

/// <summary>An <see cref="IMeterFactory"/> that records the options it was asked for.</summary>
internal sealed class RecordingMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public List<MeterOptions> Requested { get; } = [];

    public Meter Create(MeterOptions options)
    {
        Requested.Add(options);
        var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (Meter meter in _meters)
        {
            meter.Dispose();
        }
    }
}

/// <summary>Writes a few bytes, or fails or throws on request.</summary>
internal sealed class TextConverter(ReportingDependencies dependencies) : HtmlToPdfConverterBase(dependencies)
{
    public Error? Failure { get; init; }

    public bool Throws { get; init; }

    protected override async Task<Result> RenderAsync(string html, HtmlToPdfOptions options, Stream destination, CancellationToken cancellationToken)
    {
        if (Throws)
        {
            throw new InvalidOperationException("boom");
        }

        if (Failure is { } failure)
        {
            return failure;
        }

        await destination.WriteAsync(new byte[] { 1, 2, 3, 4 }, cancellationToken);
        return Result.Success();
    }
}

/// <summary>A storage factory with one store named <c>reports</c>, optionally failing every upload after reading it.</summary>
internal static class StoreSupport
{
    public static IFileStorageFactory Factory(bool failUpload)
    {
        if (!failUpload)
        {
            return InMemoryStorage.CreateFactory(new InMemoryFileStorage("reports"));
        }

        IFileStorage store = Substitute.For<IFileStorage>();
        store.StoreName.Returns("reports");
        store.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<FileUploadOptions?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var buffer = new byte[4096];
                while (await call.Arg<Stream>().ReadAsync(buffer, call.Arg<CancellationToken>()) > 0)
                {
                }

                return Result<FileReference>.Failure(StorageErrors.Unavailable("reports", "upload"));
            });

        return InMemoryStorage.CreateFactory(b => b.AddStore(new FileStoreRegistration(
            "reports", tenantScoped: false, _ => store, (_, _) => Task.FromResult(Result.Success()))));
    }

    public static IFileStorageFactory PresignFailing()
    {
        IFileStorage store = Substitute.For<IFileStorage>();
        store.StoreName.Returns("reports");
        store.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<FileUploadOptions?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var buffer = new byte[4096];
                while (await call.Arg<Stream>().ReadAsync(buffer, call.Arg<CancellationToken>()) > 0)
                {
                }

                return Result<FileReference>.Success(new FileReference { Store = "reports", Key = call.Arg<string>() });
            });
        store.CreateDownloadUrlAsync(Arg.Any<string>(), Arg.Any<PresignedDownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<PresignedRequest>.Failure(StorageErrors.Unavailable("reports", "presign"))));

        return InMemoryStorage.CreateFactory(b => b.AddStore(new FileStoreRegistration(
            "reports", tenantScoped: false, _ => store, (_, _) => Task.FromResult(Result.Success()))));
    }
}
