using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Errors;

namespace SelfAnalysisProbe;

// SK0043: exposes an Error without deriving SharedKernelException.
public sealed class ProbeException : Exception
{
    public ProbeException(Error error) => Error = error;

    public Error Error { get; }
}

public static partial class ProbeLog
{
    // SK0044: a bare numeric EventId, on a partial method the [LoggerMessage] generator completes.
    [LoggerMessage(1234, LogLevel.Information, "Probe ran.")]
    public static partial void Ran(ILogger logger);
}

public static class ProbeCode
{
    // SK0001: a direct wall-clock read.
    public static DateTime Now() => DateTime.UtcNow;

    // SK0022 (fifth shape): a raw tag key.
    public static TagList Tags() => new() { { "outcome", "success" } };

    // CA2200: rethrowing the caught exception resets the stack trace.
    public static void Rethrow()
    {
        try
        {
            Now();
        }
        catch (Exception ex)
        {
            throw ex;
        }
    }
}
