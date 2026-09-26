using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>
/// Refuses hub method invocations beyond <see cref="SharedKernelSignalROptions.InvocationRateLimit"/> before the hub
/// method runs, with <c>rate_limit.exceeded: Too many requests.</c>
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="PartitionedRateLimiter{TResource}"/> keyed by connection id serves every connection: each partition
/// is a token bucket, and the partitioned limiter replenishes all of them from a single timer and drops a bucket once
/// it has refilled and stayed idle. A limiter per connection would run a timer per connection. Nothing is stored on
/// the connection, so nothing needs removing when it closes.
/// </para>
/// <para>
/// With no <see cref="SignalRInvocationRateLimitOptions.PermitLimit"/> no limiter (and no timer) exists and every
/// invocation passes straight through. The limiter is disposed with the service provider.
/// </para>
/// </remarks>
internal sealed partial class HubInvocationRateLimitFilter : IHubFilter, IDisposable
{
    private const string TooManyRequestsMessage = "Too many requests.";

    private readonly ILogger<HubInvocationRateLimitFilter> _logger;

    /// <summary>Initializes a new instance of the <see cref="HubInvocationRateLimitFilter"/> class.</summary>
    /// <param name="options">The validated settings.</param>
    /// <param name="logger">The logger; a missing logging registration never breaks rate limiting.</param>
    public HubInvocationRateLimitFilter(
        IOptions<SharedKernelSignalROptions> options,
        ILogger<HubInvocationRateLimitFilter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger ?? NullLogger<HubInvocationRateLimitFilter>.Instance;
        Limiter = CreateLimiter(options.Value.InvocationRateLimit);
    }

    /// <summary>Gets the limiter shared by every connection, or <see langword="null"/> when the limit is off.</summary>
    internal PartitionedRateLimiter<string>? Limiter { get; }

    /// <inheritdoc />
    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (Limiter is null)
        {
            return next(invocationContext);
        }

        using (var lease = Limiter.AttemptAcquire(invocationContext.Context.ConnectionId))
        {
            if (!lease.IsAcquired)
            {
                Log.InvocationRateLimited(_logger, invocationContext.Hub.GetType().Name, invocationContext.HubMethodName);
                return ValueTask.FromException<object?>(new HubException(GetRejectionMessage(invocationContext.Context)));
            }
        }

        return next(invocationContext);
    }

    /// <inheritdoc />
    public void Dispose() => Limiter?.Dispose();

    private static PartitionedRateLimiter<string>? CreateLimiter(SignalRInvocationRateLimitOptions settings)
    {
        if (settings.PermitLimit is not { } permitLimit)
        {
            return null;
        }

        var bucket = new TokenBucketRateLimiterOptions
        {
            TokenLimit = permitLimit,
            TokensPerPeriod = permitLimit,
            ReplenishmentPeriod = settings.Window,
            QueueLimit = 0,

            // The partitioned limiter's own timer replenishes every bucket; a bucket must not start one of its own.
            AutoReplenishment = false,
        };

        return PartitionedRateLimiter.Create<string, string>(
            connectionId => RateLimitPartition.GetTokenBucketLimiter(connectionId, _ => bucket),
            StringComparer.Ordinal);
    }

    // Translated like every other client message; the rate-limit code is a client error, so it is never redacted.
    private static string GetRejectionMessage(HubCallerContext context) =>
        HubErrorMessage.For(Error.Validation(PresentationErrorCodes.RateLimitExceeded, TooManyRequestsMessage), context);

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 101,
            Level = LogLevel.Warning,
            Message = "Rate limiting refused an invocation of hub method {HubName}.{HubMethodName}.")]
        public static partial void InvocationRateLimited(ILogger logger, string hubName, string hubMethodName);
    }
}
