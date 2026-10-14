using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.Extensions;
using SharedKernel.Idempotency.EfCore.Tests.Support;
using SharedKernel.Idempotency.Redis.Extensions;
using SharedKernel.Persistence;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Concurrency;

/// <summary>
/// One host backs <see cref="IdempotencyPurpose.Request"/> with Redis and <see cref="IdempotencyPurpose.Message"/> with
/// EF Core: both providers emit through the one <c>SharedKernel.Idempotency</c> source and meter, told apart by the
/// provider tag, with no duplicate-instrument conflict.
/// </summary>
/// <remarks>REQUIRES A DOCKER DAEMON (Testcontainers PostgreSQL and Redis).</remarks>
[Collection("PostgreSqlContainer")]
public sealed class MixedProviderTelemetryTests(PostgreSqlContainerFixture postgres)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);

    private readonly RedisContainerFixture _redis = new();

    [Fact]
    public async Task OneListener_ReceivesBothProvidersStreams_DistinguishedByTheProviderTag()
    {
        await _redis.InitializeAsync();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddClock();
            services.AddRedisConnection(o => o.ConnectionString = _redis.ConnectionString);
            services.AddRedisIdempotency(p => p.ForRequests());
            services.AddEfCoreIdempotency(
                options => options.UsePostgres(TestNpgsqlDataSources.Get(postgres.ConnectionString)),
                p => p.ForMessages());

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IdempotencyDbContext>().Database.EnsureCreatedAsync();

            using var spans = ActivityRecorder.StartRecording(IdempotencyTelemetry.SourceName);
            using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
            var requests = scope.ServiceProvider.GetRequiredIdempotencyStore(IdempotencyPurpose.Request);
            var messages = scope.ServiceProvider.GetRequiredIdempotencyStore(IdempotencyPurpose.Message);
            var key = $"mixed-{Guid.NewGuid():N}";

            var request = await requests.TryBeginAsync(IdempotencyPurpose.Request, key, "fp", Ttl, CancellationToken.None);
            await requests.CompleteAsync(IdempotencyPurpose.Request, key, request.Token!, "r", Retention, CancellationToken.None);
            var message = await messages.TryBeginAsync(IdempotencyPurpose.Message, key, "fp", Ttl, CancellationToken.None);
            await messages.CompleteAsync(IdempotencyPurpose.Message, key, message.Token!, null, Retention, CancellationToken.None);

            Assert.Equal(
                2,
                metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tags.Of(IdempotencyTagKeys.Provider, "redis")));
            Assert.Equal(
                2,
                metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tags.Of(IdempotencyTagKeys.Provider, "efcore")));
            metrics.ShouldHaveMeasurement(
                IdempotencyTelemetry.OperationDurationInstrument,
                value: null,
                Tags.Of(IdempotencyTagKeys.Provider, "redis"),
                Tags.Of(IdempotencyTagKeys.Purpose, "request"),
                Tags.Of(IdempotencyTagKeys.Operation, "begin"),
                Tags.Of(IdempotencyTagKeys.Result, "started"));
            metrics.ShouldHaveMeasurement(
                IdempotencyTelemetry.OperationDurationInstrument,
                value: null,
                Tags.Of(IdempotencyTagKeys.Provider, "efcore"),
                Tags.Of(IdempotencyTagKeys.Purpose, "message"),
                Tags.Of(IdempotencyTagKeys.Operation, "complete"),
                Tags.Of(IdempotencyTagKeys.Result, "applied"));
            metrics
                .ShouldHaveInstrument(IdempotencyTelemetry.OperationDurationInstrument, "s", InstrumentKind.Histogram)
                .ShouldFollowMetricConvention();

            Assert.Equal(2, spans.RecordedActivities.Count(a => a.GetTagItem(IdempotencyTagKeys.Provider) as string == "redis"));
            Assert.Equal(2, spans.RecordedActivities.Count(a => a.GetTagItem(IdempotencyTagKeys.Provider) as string == "efcore"));
            spans.RecordedActivities.ShouldFollowSpanConvention();
        }
        finally
        {
            await _redis.DisposeAsync();
        }
    }
}
