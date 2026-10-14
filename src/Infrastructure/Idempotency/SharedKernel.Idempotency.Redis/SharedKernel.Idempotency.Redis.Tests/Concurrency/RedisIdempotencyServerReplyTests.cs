using MsOptions = Microsoft.Extensions.Options.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.Redis.Internal;
using SharedKernel.Idempotency.Redis.Options;
using SharedKernel.Idempotency.Redis.Store;
using SharedKernel.Idempotency.Redis.Tests.Support;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Concurrency;

/// <summary>
/// What the store does with replies a real Redis produces: a script or data defect is raw, a transient server state is
/// an outage. Proven against the container, not against hand-built exceptions.
/// </summary>
/// <remarks>REQUIRES A DOCKER DAEMON (Testcontainers Redis).</remarks>
[Collection("RedisContainer")]
public sealed class RedisIdempotencyServerReplyTests(RedisContainerFixture fixture)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private sealed class FixedTenantAccessor(Guid tenantId) : IRequestContextAccessor
    {
        public IRequestContext? Current { get; } =
            new SystemRequestContext([], "test", SharedKernel.Execution.Tenancy.TenantId.FromNullable(tenantId));
    }

    private static RedisIdempotencyStore CreateStore(IConnectionMultiplexer multiplexer, Guid tenantId, bool failOpen) =>
        new(
            multiplexer,
            new FixedTenantAccessor(tenantId),
            MsOptions.Create(new RedisIdempotencyOptions { AllowExecutionOnStoreUnavailable = failOpen }),
            new InMemoryLogger<RedisIdempotencyStore>(),
            TestMeterFactory.NewMetrics());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWrongTypeKey_IsADefect_ThatPropagatesRaw_EvenWhenFailOpenIsEnabled(bool failOpen)
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
        var tenant = Guid.NewGuid();
        var store = CreateStore(multiplexer, tenant, failOpen);
        var key = $"wrong-type-{Guid.NewGuid():N}";
        var redisKey = RedisIdempotencyKeyBuilder.Build(IdempotencyTenantScope.For(new(tenant)), IdempotencyPurpose.Request, key);
        await multiplexer.GetDatabase().StringSetAsync(redisKey, "not a hash");

        var exception = await Assert.ThrowsAsync<RedisServerException>(
            () => store.TryBeginAsync(IdempotencyPurpose.Request, key, "fp", Ttl, CancellationToken.None));

        Assert.False(RedisStoreUnavailableClassifier.IsStoreUnavailable(exception), exception.Message);
        Assert.Equal(0, metrics.Count(IdempotencyTelemetry.FailOpenInstrument));
        metrics.ShouldHaveMeasurement(
            IdempotencyTelemetry.OperationDurationInstrument,
            value: null,
            Tags.Of(WellKnownTagKeys.Outcome, WellKnownOutcomes.Exception));
    }

    [Fact]
    public async Task ABusyServer_IsAnOutage_AndSurfacesAsTheClassifiedExceptionWithTheBusyReplyInside()
    {
        var adminConnection = $"{fixture.ConnectionString},allowAdmin=true";
        await using var admin = await ConnectionMultiplexer.ConnectAsync(adminConnection);
        await using var blocker = await ConnectionMultiplexer.ConnectAsync(adminConnection);
        await using var client = await ConnectionMultiplexer.ConnectAsync(adminConnection);
        var server = admin.GetServer(admin.GetEndPoints()[0]);
        var store = CreateStore(client, Guid.NewGuid(), failOpen: false);

        await server.ConfigSetAsync("busy-reply-threshold", "100");
        Task? blocked = null;
        try
        {
            // A script that never returns: after the threshold the server answers every other client with BUSY.
            blocked = blocker.GetDatabase().ScriptEvaluateAsync("local i = 0 while true do i = i + 1 end");
            await Task.Delay(500);

            var exception = await Assert.ThrowsAsync<IdempotencyStoreUnavailableException>(
                () => store.TryBeginAsync(IdempotencyPurpose.Request, "busy-key", "fp", Ttl, CancellationToken.None));

            var inner = Assert.IsType<RedisServerException>(exception.InnerException);
            Assert.StartsWith("BUSY", inner.Message, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                await server.ExecuteAsync("SCRIPT", "KILL");
            }
            catch (RedisServerException)
            {
                // No script is running any more.
            }

            if (blocked is not null)
            {
                try
                {
                    await blocked;
                }
                catch (RedisServerException)
                {
                    // The killed script reports an error to its caller.
                }
            }

            await server.ConfigSetAsync("busy-reply-threshold", "5000");
        }
    }

}
