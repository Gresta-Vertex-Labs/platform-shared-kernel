using System.Net;
using Amazon.Runtime;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Health;
using SharedKernel.Storage.S3.Diagnostics;
using SharedKernel.Storage.S3.Internal;
using SharedKernel.Storage.S3.Tests.Infrastructure;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Storage.S3.Tests;

/// <summary>
/// The log statements of the S3 provider: each of 8100-8105 pinned (template, level, exception parameter) and the
/// ones a real MinIO store raises checked for category, sub-block and leaks.
/// </summary>
public sealed class S3StorageLogTests
{
    private const string Prefix = "Store '{Store}' on bucket '{Bucket}' ";

    private static InMemoryLogger Logger() => new();

    [Fact]
    public void The_event_ids_sit_in_the_second_sub_block_of_the_storage_block()
    {
        int[] ids =
        [
            S3StorageLoggingEventIds.AccessDenied,
            S3StorageLoggingEventIds.Unavailable,
            S3StorageLoggingEventIds.ProviderError,
            S3StorageLoggingEventIds.ExpectedFailure,
            S3StorageLoggingEventIds.ProbeFailed,
            S3StorageLoggingEventIds.WrongRegion,
        ];

        ids.Should().Equal(8100, 8101, 8102, 8103, 8104, 8105);
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Storage, 1));
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Access_denied_is_a_warning_without_an_exception()
    {
        InMemoryLogger logger = Logger();

        S3StorageLog.AccessDenied(logger, "files", "bucket-a", "upload", HttpStatusCode.Forbidden, "AccessDenied", "req-1");

        LogRecord record = logger.Records.ShouldHaveLogged(new EventId(8100), LogLevel.Warning);
        record.Template.Should().Be(Prefix + "denied operation '{Operation}' with status {StatusCode}, error code '{ErrorCode}' and request '{RequestId}'.");
        record.Exception.Should().BeNull();
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public void Unavailable_is_a_warning_with_the_exception_last()
    {
        InMemoryLogger logger = Logger();

        S3StorageLog.Unavailable(logger, "files", "bucket-a", "upload", null, null, null, new HttpRequestException("down"));

        LogRecord record = logger.Records.ShouldHaveLogged(new EventId(8101), LogLevel.Warning);
        record.Template.Should().Be(Prefix + "was unavailable for operation '{Operation}' with status {StatusCode}, error code '{ErrorCode}' and request '{RequestId}'.");
        logger.Records.ShouldHaveLoggedWithException(new EventId(8101), typeof(HttpRequestException));
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public void A_provider_error_is_an_error_with_the_exception_last()
    {
        InMemoryLogger logger = Logger();

        S3StorageLog.ProviderError(logger, "files", "bucket-a", "copy", HttpStatusCode.BadRequest, "Bad", "req-1", new AmazonServiceException("x"));

        LogRecord record = logger.Records.ShouldHaveLogged(new EventId(8102), LogLevel.Error);
        record.Template.Should().Be(Prefix + "rejected operation '{Operation}' with status {StatusCode}, error code '{ErrorCode}' and request '{RequestId}'.");
        logger.Records.ShouldHaveLoggedWithException(new EventId(8102), typeof(AmazonServiceException));
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public void An_expected_failure_is_logged_at_debug_without_an_exception()
    {
        InMemoryLogger logger = Logger();

        S3StorageLog.ExpectedFailure(logger, "files", "bucket-a", "get_properties", HttpStatusCode.NotFound, "NoSuchKey");

        LogRecord record = logger.Records.ShouldHaveLogged(new EventId(8103), LogLevel.Debug);
        record.Template.Should().Be(Prefix + "answered operation '{Operation}' with status {StatusCode} and error code '{ErrorCode}'.");
        record.Exception.Should().BeNull();
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public void A_failed_probe_is_a_warning_with_the_exception_last()
    {
        InMemoryLogger logger = Logger();

        S3StorageLog.ProbeFailed(logger, "files", "bucket-a", new AmazonServiceException("x"));

        LogRecord record = logger.Records.ShouldHaveLogged(new EventId(8104), LogLevel.Warning);
        record.Template.Should().Be(Prefix + "failed its reachability probe.");
        logger.Records.ShouldHaveLoggedWithException(new EventId(8104), typeof(AmazonServiceException));
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public void A_wrong_region_is_an_error_with_the_exception_last_and_names_the_remedy()
    {
        InMemoryLogger logger = Logger();

        S3StorageLog.WrongRegion(logger, "files", "bucket-a", "PermanentRedirect", "req-1", new AmazonServiceException("x"));

        LogRecord record = logger.Records.ShouldHaveLogged(new EventId(8105), LogLevel.Error);
        record.Template.Should().Be(
            Prefix + "is not served by the configured endpoint or region with error code '{ErrorCode}' and request '{RequestId}', so set the connection Region to the region of the bucket.");
        logger.Records.ShouldHaveLoggedWithException(new EventId(8105), typeof(AmazonServiceException));
        logger.Records.ShouldFollowLogConvention();
    }
}

/// <summary>The statements a store raises while talking to MinIO, captured through the in-memory logger factory.</summary>
[Collection(MinioCollection.Name)]
public sealed class StoreLoggingTests(MinioFixture minio)
{
    private const string Category = "SharedKernel.Storage.S3.Internal.S3FileStorage";

    private ServiceProvider CreateHost(InMemoryLoggerFactory loggers, Action<Dictionary<string, string?>>? adjust = null, Action<S3StorageBuilder>? extraStores = null) =>
        minio.CreateHost(
            adjust,
            extraStores,
            services => services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(loggers)));

    private static IReadOnlyList<LogRecord> RecordsOf(InMemoryLoggerFactory loggers) => loggers.GetLogger(Category).Records;

    [Fact]
    public async Task A_missing_object_logs_the_expected_failure_at_debug_under_the_s3_store_category()
    {
        var loggers = new InMemoryLoggerFactory();
        using ServiceProvider host = CreateHost(loggers);
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");
        string key = TestData.UniqueKey("secret-name.txt");

        await files.GetPropertiesAsync(key);

        loggers.ShouldHaveCategory(typeof(S3FileStorage));
        RecordsOf(loggers).ShouldHaveLogged(new EventId(S3StorageLoggingEventIds.ExpectedFailure), LogLevel.Debug)
            .Message.Should().Contain("'files'").And.Contain($"'{MinioFixture.BucketA}'");
        RecordsOf(loggers).ShouldFollowLogConvention().ShouldNotLeak("secret-name", minio.ServiceUrl, "127.0.0.1");
    }

    [Fact]
    public async Task Bad_credentials_log_access_denied_as_a_warning()
    {
        var loggers = new InMemoryLoggerFactory();
        using ServiceProvider host = CreateHost(loggers, settings => settings["SharedKernel:Storage:S3:SecretAccessKey"] = "not-the-secret");
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");

        (await files.GetPropertiesAsync("a")).Error.Code.Should().Be(StorageErrorCodes.AccessDenied);

        RecordsOf(loggers).ShouldHaveLogged(new EventId(S3StorageLoggingEventIds.AccessDenied), LogLevel.Warning);
        RecordsOf(loggers).ShouldFollowLogConvention().ShouldNotLeak(minio.ServiceUrl, "not-the-secret", minio.SecretKey);
    }

    [Fact]
    public async Task A_missing_bucket_logs_a_provider_error_with_the_exception()
    {
        var loggers = new InMemoryLoggerFactory();
        using ServiceProvider host = CreateHost(loggers, extraStores: s3 => s3.AddStore("ghost", o => o.Bucket = "no-such-bucket"));
        IFileStorage ghost = host.GetRequiredKeyedService<IFileStorage>("ghost");

        (await ghost.DeleteAsync("a")).Error.Code.Should().Be(StorageErrorCodes.ProviderError);

        RecordsOf(loggers).ShouldHaveLogged(new EventId(S3StorageLoggingEventIds.ProviderError), LogLevel.Error);
        RecordsOf(loggers).ShouldHaveLoggedWithException(new EventId(S3StorageLoggingEventIds.ProviderError), typeof(AmazonServiceException));
        RecordsOf(loggers).ShouldFollowLogConvention();
    }

    [Fact]
    public async Task An_outage_logs_unavailable_and_a_failed_probe_with_the_exception_and_leaks_nothing()
    {
        var loggers = new InMemoryLoggerFactory();
        using ServiceProvider host = CreateHost(
            loggers,
            settings =>
            {
                settings["SharedKernel:Storage:S3:ServiceUrl"] = "http://127.0.0.1:1";
                settings["SharedKernel:Storage:S3:MaxRetries"] = "0";
                settings["SharedKernel:Storage:S3:RequestTimeout"] = "00:00:03";
            });
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");

        (await files.ExistsAsync("secret-folder/a")).Error.Code.Should().Be(StorageErrorCodes.Unavailable);
        await host.GetRequiredReadinessProbe(StorageReadinessProbeNames.ForStore("files")).ProbeAsync();

        IReadOnlyList<LogRecord> records = RecordsOf(loggers);
        records.ShouldHaveLogged(new EventId(S3StorageLoggingEventIds.Unavailable), LogLevel.Warning);
        records.ShouldHaveLoggedWithException(new EventId(S3StorageLoggingEventIds.Unavailable), typeof(Exception));
        records.ShouldHaveLogged(new EventId(S3StorageLoggingEventIds.ProbeFailed), LogLevel.Warning);
        records.ShouldFollowLogConvention()
            .ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Storage, 1)
            .ShouldNotLeak("secret-folder");

        // The rendered messages and structured values never name the endpoint; the exception object may.
        string[] rendered = [.. records.Select(r => r.Message), .. records.SelectMany(r => r.State ?? []).Select(p => p.Value?.ToString() ?? string.Empty)];
        rendered.Should().AllSatisfy(s => s.Should().NotContain("127.0.0.1").And.NotContain("http://"));
    }
}
