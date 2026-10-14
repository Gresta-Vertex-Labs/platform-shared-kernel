using System.Diagnostics;
using Grpc.Core;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Presentation.Grpc.Tests.TestSupport;

/// <summary>
/// Calls under a trace the test owns and returns the framework's server span (<c>HttpRequestIn</c>) of that trace:
/// <c>Microsoft.AspNetCore</c> is a static source shared by tests running in parallel.
/// </summary>
internal static class ServerSpans
{
    public const string SourceName = "Microsoft.AspNetCore";

    public const string ServerSpanName = "Microsoft.AspNetCore.Hosting.HttpRequestIn";

    /// <summary>Starts a recorder on the framework's source.</summary>
    public static ActivityRecorder Record() => ActivityRecorder.StartRecording(SourceName);

    /// <summary>Returns request metadata carrying a fresh W3C trace, and that trace's id.</summary>
    public static Metadata NewTrace(out ActivityTraceId traceId)
    {
        traceId = ActivityTraceId.CreateRandom();
        return new Metadata { { "traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01" } };
    }

    /// <summary>Waits for the server span of <paramref name="traceId"/> to stop: it stops when the call finishes.</summary>
    public static async Task<Activity> WaitForServerSpanAsync(this ActivityRecorder recorder, ActivityTraceId traceId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (true)
        {
            var span = recorder.ForTrace(traceId).FirstOrDefault(activity => activity.OperationName == ServerSpanName);
            if (span is not null)
            {
                return span;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException("The server span of the call never stopped.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }
}
