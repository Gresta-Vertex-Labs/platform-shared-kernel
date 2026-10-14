using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using FluentAssertions;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage.S3.Diagnostics;
using SharedKernel.Storage.S3.Tests.Infrastructure;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Storage.S3.Tests;

/// <summary>
/// Spans and instruments of the S3 provider against real MinIO, asserted through the shared recorders: success,
/// a <c>Result</c> failure, an outage, cancellation, an unexpected exception and the readiness probe.
/// </summary>
[Collection(MinioCollection.Name)]
public sealed class TelemetryTests(MinioFixture minio)
{
    private const string Duration = StorageTelemetryNames.OperationDurationInstrumentName;
    private const string Bytes = StorageTelemetryNames.BytesInstrumentName;

    private static readonly string[] DurationTags =
    [
        StorageTagKeys.Store, StorageTagKeys.Operation, StorageTagKeys.Provider, WellKnownTagKeys.Outcome,
    ];

    private static string? Tag(RecordedMeasurement measurement, string key) =>
        measurement.TryGetTag(key, out object? value) ? value as string : null;

    private static RecordedMeasurement SingleDuration(MetricRecorder metrics, string operation) =>
        metrics.MeasurementsOf(Duration).Where(m => Tag(m, StorageTagKeys.Operation) == operation).Should().ContainSingle().Subject;

    private static Dictionary<string, string?> Unreachable(Dictionary<string, string?> settings)
    {
        settings["SharedKernel:Storage:S3:ServiceUrl"] = "http://127.0.0.1:1";
        settings["SharedKernel:Storage:S3:MaxRetries"] = "0";
        settings["SharedKernel:Storage:S3:RequestTimeout"] = "00:00:03";
        return settings;
    }

    [Fact]
    public async Task A_successful_upload_and_download_record_unset_spans_a_duration_and_byte_counts()
    {
        using ServiceProvider host = minio.CreateHost();
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");
        string key = TestData.UniqueKey();
        using var activities = ActivityRecorder.StartRecording(StorageTelemetryNames.SourceName);
        using var metrics = MetricRecorder.StartRecording(StorageTelemetryNames.MeterName);

        (await files.UploadAsync(key, new MemoryStream(new byte[100]))).IsSuccess.Should().BeTrue();
        await using (FileDownload download = (await files.DownloadAsync(key)).Ok())
        {
            await download.Content.CopyToAsync(Stream.Null);
        }

        Activity upload = activities.RecordedActivities.ShouldHaveSingleSpan("storage upload");
        upload.ShouldBeRecordedSuccess()
            .ShouldHaveKind(ActivityKind.Client)
            .ShouldHaveSource(StorageTelemetryNames.SourceName)
            .ShouldHaveTag(StorageTagKeys.Store, "files")
            .ShouldHaveTag(StorageTagKeys.Operation, "upload")
            .ShouldHaveTag(StorageTagKeys.Provider, "S3");
        activities.RecordedActivities.ShouldHaveSingleSpan("storage download").ShouldBeRecordedSuccess();

        RecordedMeasurement duration = SingleDuration(metrics, "upload");
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Success);
        duration.Value.Should().BeGreaterThan(0);
        duration.Tags.Select(t => t.Key).Should().BeEquivalentTo(DurationTags);

        metrics.MeasurementsOf(Bytes).Should().Contain(m => Tag(m, StorageTagKeys.Direction) == "upload" && m.Value == 100);
        metrics.MeasurementsOf(Bytes).Should().Contain(m => Tag(m, StorageTagKeys.Direction) == "download" && m.Value == 100);
        metrics.MeasurementsOf(Bytes).Should().OnlyContain(m => Tag(m, StorageTagKeys.Store) == "files" && Tag(m, StorageTagKeys.Provider) == "S3");
        activities.ShouldHaveStoppedAll();
    }

    [Fact]
    public async Task A_missing_object_is_a_recorded_not_found_failure_with_the_code_and_the_error_type()
    {
        using ServiceProvider host = minio.CreateHost();
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");
        string key = TestData.UniqueKey();
        using var activities = ActivityRecorder.StartRecording(StorageTelemetryNames.SourceName);
        using var metrics = MetricRecorder.StartRecording(StorageTelemetryNames.MeterName);

        Result<FileProperties> result = await files.GetPropertiesAsync(key);

        result.Error.Code.Should().Be(StorageErrorCodes.NotFound);
        activities.RecordedActivities.ShouldHaveSingleSpan("storage get_properties")
            .ShouldBeRecordedFailure(StorageErrors.NotFound("files", key))
            .ShouldHaveTag(WellKnownTagKeys.ErrorCode, StorageErrorCodes.NotFound)
            .ShouldHaveTag(WellKnownTagKeys.ErrorType, "NotFound");
        RecordedMeasurement duration = SingleDuration(metrics, "get_properties");
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Failure);
        Tag(duration, WellKnownTagKeys.ErrorCode).Should().Be(StorageErrorCodes.NotFound);
        Tag(duration, WellKnownTagKeys.ErrorType).Should().Be("NotFound");
        duration.Tags.Select(t => t.Key).Should().BeEquivalentTo([.. DurationTags, WellKnownTagKeys.ErrorType, WellKnownTagKeys.ErrorCode]);
    }

    [Fact]
    public async Task A_request_refused_before_any_io_is_still_a_recorded_failure_without_the_key()
    {
        using ServiceProvider host = minio.CreateHost();
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");
        using var activities = ActivityRecorder.StartRecording(StorageTelemetryNames.SourceName);
        using var metrics = MetricRecorder.StartRecording(StorageTelemetryNames.MeterName);

        Result<FileReference> result = await files.UploadAsync(
            TestData.UniqueKey("secret-name.txt"),
            new ForwardOnlyStream([1]),
            new FileUploadOptions { ChecksumSha256 = TestData.Sha256([1]) });

        result.Error.Code.Should().Be(StorageErrorCodes.InvalidRequest);
        activities.RecordedActivities.ShouldHaveSingleSpan("storage upload")
            .ShouldBeRecordedFailure(result.Error)
            .ShouldHaveTag(WellKnownTagKeys.ErrorType, "Validation")
            .ShouldNotLeak("secret-name");
        RecordedMeasurement duration = SingleDuration(metrics, "upload");
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Failure);
        Tag(duration, WellKnownTagKeys.ErrorCode).Should().Be(StorageErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task An_outage_is_a_recorded_unavailable_failure()
    {
        using ServiceProvider host = minio.CreateHost(settings => Unreachable(settings));
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");
        using var activities = ActivityRecorder.StartRecording(StorageTelemetryNames.SourceName);
        using var metrics = MetricRecorder.StartRecording(StorageTelemetryNames.MeterName);

        Result<bool> exists = await files.ExistsAsync("a");

        exists.Error.Code.Should().Be(StorageErrorCodes.Unavailable);
        activities.RecordedActivities.ShouldHaveSingleSpan("storage exists")
            .ShouldBeRecordedFailure(StorageErrors.Unavailable("files", "exists"));
        RecordedMeasurement duration = SingleDuration(metrics, "exists");
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Failure);
        Tag(duration, WellKnownTagKeys.ErrorType).Should().Be("Unavailable");
        Tag(duration, WellKnownTagKeys.ErrorCode).Should().Be(StorageErrorCodes.Unavailable);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_with_no_error_status_and_still_records_a_duration()
    {
        using ServiceProvider host = minio.CreateHost();
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var activities = ActivityRecorder.StartRecording(StorageTelemetryNames.SourceName);
        using var metrics = MetricRecorder.StartRecording(StorageTelemetryNames.MeterName);

        Func<Task> download = () => files.DownloadAsync("a", cancellationToken: cancelled.Token);

        await download.Should().ThrowAsync<OperationCanceledException>();
        activities.RecordedActivities.ShouldHaveSingleSpan("storage download").ShouldBeRecordedCancellation();
        RecordedMeasurement duration = SingleDuration(metrics, "download");
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Canceled);
        duration.Tags.Select(t => t.Key).Should().BeEquivalentTo(DurationTags);
    }

    [Fact]
    public async Task An_unexpected_exception_is_recorded_in_full_rethrown_and_leaves_the_callers_stream_open()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(minio.Settings()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelStorage()
            .AddS3Compatible("Broken", configuration, _ => (DispatchProxy.Create<IAmazonS3, ThrowingClient>(), new S3Compatibility()))
            .AddStore("files");
        using ServiceProvider host = services.BuildServiceProvider();
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");
        using var content = new TrackedStream();
        using var activities = ActivityRecorder.StartRecording(StorageTelemetryNames.SourceName);
        using var metrics = MetricRecorder.StartRecording(StorageTelemetryNames.MeterName);

        Func<Task> upload = () => files.UploadAsync(TestData.UniqueKey(), content, new FileUploadOptions { ContentLength = 3 });

        await upload.Should().ThrowAsync<InvalidOperationException>().WithMessage("The client failed.");
        activities.RecordedActivities.ShouldHaveSingleSpan("storage upload")
            .ShouldBeRecordedException(typeof(InvalidOperationException))
            .ShouldHaveTag(StorageTagKeys.Provider, "Broken");
        RecordedMeasurement duration = SingleDuration(metrics, "upload");
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Exception);
        Tag(duration, WellKnownTagKeys.ErrorType).Should().Be(typeof(InvalidOperationException).FullName);
        duration.Tags.Select(t => t.Key).Should().NotContain(WellKnownTagKeys.ErrorCode);
        content.Disposed.Should().BeFalse("the upload stream belongs to the caller");
    }

    [Fact]
    public async Task The_readiness_probe_records_a_success_and_a_failure()
    {
        using ServiceProvider host = minio.CreateHost(extraStores: s3 => s3.AddStore("ghost", o => o.Bucket = "no-such-bucket"));
        using var activities = ActivityRecorder.StartRecording(StorageTelemetryNames.SourceName);
        using var metrics = MetricRecorder.StartRecording(StorageTelemetryNames.MeterName);

        await host.GetRequiredReadinessProbe(StorageReadinessProbeNames.ForStore("files")).ProbeAsync();
        await host.GetRequiredReadinessProbe(StorageReadinessProbeNames.ForStore("ghost")).ProbeAsync();

        Activity[] probes = [.. activities.RecordedActivities.Where(a => a.DisplayName == "storage probe")];
        probes.Should().HaveCount(2);
        probes.Single(a => (string?)a.GetTagItem(StorageTagKeys.Store) == "files").ShouldBeRecordedSuccess();
        probes.Single(a => (string?)a.GetTagItem(StorageTagKeys.Store) == "ghost")
            .ShouldBeRecordedFailure(StorageErrors.Unavailable("ghost", "probe"));
        metrics.MeasurementsOf(Duration).Select(m => Tag(m, WellKnownTagKeys.Outcome))
            .Should().BeEquivalentTo(WellKnownOutcomes.Success, WellKnownOutcomes.Failure);
    }

    [Fact]
    public async Task Instruments_follow_the_metric_conventions_and_leak_no_key_bucket_or_endpoint()
    {
        using ServiceProvider host = minio.CreateHost();
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");
        string key = TestData.UniqueKey("secret-name.txt");
        using var activities = ActivityRecorder.StartRecording(StorageTelemetryNames.SourceName);
        using var metrics = MetricRecorder.StartRecording(StorageTelemetryNames.MeterName);

        await files.UploadAsync(key, new MemoryStream([1, 2, 3]));
        await files.GetPropertiesAsync(TestData.UniqueKey("secret-name.txt"));
        await files.DeleteAsync(key);

        metrics
            .ShouldHaveInstrument("sharedkernel.storage.operation.duration", unit: "s", kind: InstrumentKind.Histogram)
            .ShouldHaveInstrument("sharedkernel.storage.bytes", unit: "By", kind: InstrumentKind.Counter)
            .ShouldFollowMetricConvention()
            .ShouldUseOutcomeVocabulary(Duration)
            .ShouldNotHaveTagKey(Bytes, WellKnownTagKeys.Outcome)
            .ShouldHaveTagSet(Bytes, StorageTagKeys.Store, StorageTagKeys.Provider, StorageTagKeys.Direction)
            .ShouldNotLeak("secret-name", MinioFixture.BucketA, minio.ServiceUrl, "127.0.0.1");
        metrics.Instruments.Select(i => i.Name).Distinct().Should().BeEquivalentTo(
            "sharedkernel.storage.operation.duration",
            "sharedkernel.storage.bytes");
        activities.RecordedActivities.ShouldFollowSpanConvention()
            .ShouldNotLeak("secret-name", MinioFixture.BucketA, minio.ServiceUrl, "127.0.0.1");
    }

    [Fact]
    public void The_public_names_are_pinned_literally()
    {
        StorageTelemetryNames.SourceName.Should().Be("SharedKernel.Storage");
        StorageTelemetryNames.MeterName.Should().Be("SharedKernel.Storage");
        StorageTelemetryNames.OperationDurationInstrumentName.Should().Be("sharedkernel.storage.operation.duration");
        StorageTelemetryNames.BytesInstrumentName.Should().Be("sharedkernel.storage.bytes");
        StorageTelemetry.Name.Should().Be("SharedKernel.Storage");
        StorageTelemetry.Source.Name.Should().Be("SharedKernel.Storage");
        StorageTelemetry.Source.Version.Should().NotBeNullOrEmpty();
    }

    /// <summary>A client whose every operation fails with a defect, to drive the unexpected-exception path.</summary>
    public class ThrowingClient : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name.StartsWith("get_", StringComparison.Ordinal) || targetMethod.Name == "Dispose")
            {
                return targetMethod.ReturnType.IsValueType && targetMethod.ReturnType != typeof(void)
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;
            }

            throw new InvalidOperationException("The client failed.");
        }
    }

    /// <summary>A seekable stream that records whether the code under test disposed it.</summary>
    private sealed class TrackedStream : MemoryStream
    {
        public TrackedStream()
            : base([1, 2, 3])
        {
        }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}

/// <summary>How the provider telemetry is created: from the host's <see cref="IMeterFactory"/>, once per host.</summary>
[Collection(MinioCollection.Name)]
public sealed class TelemetryRegistrationTests(MinioFixture minio)
{
    [Fact]
    public void Registration_adds_the_meter_factory_and_one_telemetry_singleton()
    {
        using ServiceProvider host = minio.CreateHost();

        host.GetService<IMeterFactory>().Should().NotBeNull();
        host.GetRequiredService<StorageTelemetry>().Should().BeSameAs(host.GetRequiredService<StorageTelemetry>());
    }

    [Fact]
    public void A_second_connection_does_not_register_a_second_telemetry()
    {
        var services = new ServiceCollection();
        Microsoft.Extensions.Configuration.IConfiguration configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        IStorageBuilder storage = services.AddSharedKernelStorage();

        storage.AddS3Compatible("One", configuration, _ => throw new InvalidOperationException("unused"));
        storage.AddS3Compatible("Two", configuration, _ => throw new InvalidOperationException("unused"));

        services.Count(d => d.ServiceType == typeof(StorageTelemetry)).Should().Be(1);
    }

    [Fact]
    public void A_host_that_already_registered_a_meter_factory_keeps_it_and_the_provider_uses_it()
    {
        var factory = new CountingMeterFactory();

        using ServiceProvider host = minio.CreateHost(configureServices: services => services.AddSingleton<IMeterFactory>(factory));

        host.GetRequiredService<IMeterFactory>().Should().BeSameAs(factory);
        host.GetRequiredService<StorageTelemetry>().Should().NotBeNull();
        factory.Created.Should().Contain(StorageTelemetryNames.MeterName);
    }

    [Fact]
    public void Two_hosts_do_not_share_a_meter()
    {
        using ServiceProvider first = minio.CreateHost();
        using ServiceProvider second = minio.CreateHost();

        first.GetRequiredService<IMeterFactory>().Should().NotBeSameAs(second.GetRequiredService<IMeterFactory>());
        first.GetRequiredService<StorageTelemetry>().Should().NotBeSameAs(second.GetRequiredService<StorageTelemetry>());
    }

    private sealed class CountingMeterFactory : IMeterFactory
    {
        private readonly List<string> _created = [];

        public IReadOnlyList<string> Created => _created;

        public Meter Create(MeterOptions options)
        {
            _created.Add(options.Name);
            return new Meter(options);
        }

        public void Dispose()
        {
        }
    }
}
