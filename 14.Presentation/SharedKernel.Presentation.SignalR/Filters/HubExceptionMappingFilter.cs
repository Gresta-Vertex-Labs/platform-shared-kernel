using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>
/// Wraps every hub method invocation and converts thrown exceptions into safe
/// <see cref="HubException"/> instances.
/// </summary>
/// <remarks>
/// Known <see cref="SharedKernelException"/> subtypes (<c>01.Core</c>) are rethrown as
/// <see cref="HubException"/> using <see cref="SharedKernelException.Error"/>'s
/// <c>Message</c> as the message — caller-safe, no stack trace, no internal type names. Unknown
/// exceptions are logged at <see cref="LogLevel.Error"/> and rethrown as a generic
/// <see cref="HubException"/> — SignalR already redacts unhandled exception detail from clients
/// by default; this filter is the explicit, auditable safety net rather than relying on that
/// default silently. A non-<see cref="HubException"/> must never cross this filter boundary.
/// </remarks>
public sealed partial class HubExceptionMappingFilter : IHubFilter
{
    private const string UnexpectedErrorMessage = "An unexpected error occurred.";

    private readonly ILogger<HubExceptionMappingFilter> _logger;

    /// <summary>
    /// Initialises a new <see cref="HubExceptionMappingFilter"/>.
    /// </summary>
    /// <param name="logger">The logger used to record unknown exceptions before redaction.</param>
    public HubExceptionMappingFilter(ILogger<HubExceptionMappingFilter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Invokes the hub method, mapping any thrown exception to a safe <see cref="HubException"/>.
    /// </summary>
    /// <param name="invocationContext">The context for the current hub method invocation.</param>
    /// <param name="next">The next delegate in the invocation pipeline.</param>
    /// <returns>The hub method's result, or rethrows a <see cref="HubException"/> on failure.</returns>
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (HubException)
        {
            // Already a safe, client-facing exception — pass it through unchanged.
            throw;
        }
        catch (SharedKernelException sharedKernelException)
        {
            throw new HubException(sharedKernelException.Error.Message);
        }
        catch (Exception exception)
        {
            Log.UnhandledHubException(_logger, invocationContext.HubMethodName, exception);
            throw new HubException(UnexpectedErrorMessage);
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="HubExceptionMappingFilter"/>.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 100,
            Level = LogLevel.Error,
            Message = "Unhandled exception in hub method {HubMethodName}.")]
        public static partial void UnhandledHubException(ILogger logger, string hubMethodName, Exception exception);
    }
}
