using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Presentation.WebApi.Tests.TestSupport;

/// <summary>
/// Sends requests under a trace the test owns and returns the framework's server span (<c>HttpRequestIn</c>) of that
/// trace, so a test asserts on the span its own request produced although <c>Microsoft.AspNetCore</c> is a static
/// source shared by tests running in parallel.
/// </summary>
internal static class ServerSpans
{
    public const string SourceName = "Microsoft.AspNetCore";

    public const string ServerSpanName = "Microsoft.AspNetCore.Hosting.HttpRequestIn";

    /// <summary>Starts a recorder on the framework's source.</summary>
    public static ActivityRecorder Record() => ActivityRecorder.StartRecording(SourceName);

    /// <summary>Sends <paramref name="method"/> <paramref name="path"/> with a fresh W3C trace and waits for its server span to stop.</summary>
    public static async Task<ServerCall> SendAsync(
        this WebApplication app,
        ActivityRecorder recorder,
        HttpMethod method,
        string path,
        Action<HttpRequestMessage>? configure = null)
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        configure?.Invoke(request);

        var response = await app.GetTestClient().SendAsync(request);
        var span = await WaitForServerSpanAsync(recorder, traceId);
        return new ServerCall(response, span);
    }

    /// <summary>Sends a GET.</summary>
    public static Task<ServerCall> GetAsync(this WebApplication app, ActivityRecorder recorder, string path) =>
        app.SendAsync(recorder, HttpMethod.Get, path);

    /// <summary>
    /// Sends a GET to a host whose request throws out of the pipeline (no exception middleware): <see cref="TestServer"/>
    /// rethrows it to the client, and the server span of the request is returned.
    /// </summary>
    public static async Task<Activity> GetUnhandledAsync(this WebApplication app, ActivityRecorder recorder, string path)
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");

        try
        {
            using var response = await app.GetTestClient().SendAsync(request);
        }
        catch (Exception)
        {
            // Expected: the exception left the pipeline.
        }

        return await WaitForServerSpanAsync(recorder, traceId);
    }

    private static async Task<Activity> WaitForServerSpanAsync(ActivityRecorder recorder, ActivityTraceId traceId)
    {
        // The server span stops when the request finishes, which can be just after the client has the response.
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
                throw new InvalidOperationException("The server span of the request never stopped.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }
}

/// <summary>A response and the server span of its request.</summary>
internal sealed class ServerCall(HttpResponseMessage response, Activity span) : IDisposable
{
    public HttpResponseMessage Response { get; } = response;

    public Activity Span { get; } = span;

    public void Dispose() => Response.Dispose();
}
