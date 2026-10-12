using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.ExceptionHandling;

/// <summary>
/// SK.14.EdgeErrorContext: a handled exception leaves exactly one <c>exception</c> event on the server span with the
/// instrumentation at its defaults (the platform's handler is the only recorder), the framework's diagnostics for it
/// stay suppressed, a returned error is as visible as a thrown one, and an exception a service's own
/// <c>IExceptionHandler</c> handles first is left to that service.
/// </summary>
public sealed class ServerSpanExactlyOnceTests
{
    private static readonly Error Unexpected = Error.Unexpected("order.broken", "The order is broken.");

    [Fact]
    public async Task HandledException_WithDefaultInstrumentation_LeavesExactlyOneExceptionEvent()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await WebApiTestHost.StartAsync(
            app => app.MapGet("/boom", IResult () => throw new InvalidOperationException("boom")),
            builder => builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddAspNetCoreInstrumentation()));

        using var call = await app.GetAsync(recorder, "/boom");

        call.Response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        call.Span.Events.Count(e => e.Name == "exception").Should().Be(1);
        call.Span.ShouldBeRecordedException(typeof(InvalidOperationException), ErrorCodesUnexpected);
    }

    [Fact]
    public async Task HandledException_WithRecordExceptionTurnedOn_StillHasExactlyOneExceptionEvent()
    {
        // D-02b, pinned against OpenTelemetry.Instrumentation.AspNetCore 1.15.2 on .NET 10. The kernel never turns
        // RecordException on (13.ServiceDefaults keeps it off), but a service that does gets no second event for what an
        // exception middleware handled: the instrumentation records only exceptions that leave the pipeline unhandled.
        using var recorder = ServerSpans.Record();
        await using var app = await WebApiTestHost.StartAsync(
            app => app.MapGet("/boom", IResult () => throw new InvalidOperationException("boom")),
            builder => builder.Services.AddOpenTelemetry().WithTracing(
                tracing => tracing.AddAspNetCoreInstrumentation(options => options.RecordException = true)));

        using var call = await app.GetAsync(recorder, "/boom");

        call.Span.Events.Count(e => e.Name == "exception").Should().Be(1);

        // The control: the same instrumentation records an exception nobody handled, so it is wired and able to add an
        // event; for an exception the exception middleware handled it adds none,
        // which leaves the platform's single event the only one.
        await using var unhandled = await StartUnhandledAsync();
        var unhandledSpan = await unhandled.GetUnhandledAsync(recorder, "/probe");
        unhandledSpan.Events.Count(e => e.Name == "exception").Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ServiceHandlerThatHandlesFirst_LeavesTheSpanUntouchedByTheKernel()
    {
        // Documented behaviour: the platform handler is the fallback, so what a service's IExceptionHandler handles is
        // that service's to record.
        using var recorder = ServerSpans.Record();
        await using var app = await WebApiTestHost.StartAsync(
            app => app.MapGet("/teapot", IResult () => throw new TeapotException()),
            builder => builder.Services.AddExceptionHandler<TeapotHandler>());

        using var call = await app.GetAsync(recorder, "/teapot");

        call.Response.StatusCode.Should().Be((HttpStatusCode)StatusCodes.Status418ImATeapot);
        call.Span.ShouldBeRecordedSuccess();
    }

    [Fact]
    public async Task TheExceptionMiddlewaresMetric_StillCountsWhatThePlatformHandled_OnlyItsLogAndDiagnosticEventsAreSuppressed()
    {
        // D-03, pinned against .NET 10: SuppressDiagnosticsCallback silences the middleware's log and diagnostic events
        // but not the aspnetcore.diagnostics.exceptions counter, so no signal is lost by suppressing: the counter, the 5xx
        // counts of http.server.request.duration and the server span's error.type/error.code all stay. The control host
        // (a plain UseExceptionHandler) proves the probe sees a measurement at all.
        using var recorder = SharedKernel.Testing.Telemetry.MetricRecorder.StartRecording("Microsoft.AspNetCore.Diagnostics");
        await using var platform = await WebApiTestHost.StartAsync(app => app.MapGet("/probe", IResult () => throw new SuppressionProbeException()));
        await using var control = await StartPlainExceptionHandlerAsync();

        using var handledByPlatform = await platform.GetTestClient().GetAsync("/probe");
        using var handledByControl = await control.GetTestClient().GetAsync("/probe");

        handledByPlatform.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var probes = recorder.Measurements.Where(m => m.InstrumentName == "aspnetcore.diagnostics.exceptions").ToList();
        probes.Should().Contain(m => m.Tags.Any(t => Equals(t.Value, typeof(ControlProbeException).FullName)));
        probes.Should().Contain(
            m => m.Tags.Any(t => Equals(t.Value, typeof(SuppressionProbeException).FullName))
                && m.Tags.Any(t => Equals(t.Value, "handled")),
            "the platform handled this exception; the framework still counts it as handled");
    }
    [Fact]
    public async Task ReturnedServerCategoryError_FailsTheServerSpan_LikeAThrownOne()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await StartReturningAsync();

        using var unavailable = await app.GetAsync(recorder, "/unavailable");
        using var unexpected = await app.GetAsync(recorder, "/unexpected");

        await unavailable.Response.ShouldBeProblemAsync(StatusCodes.Status503ServiceUnavailable, "search.unreachable");
        unavailable.Span.ShouldBeRecordedFailure(TestErrors.SearchUnreachable);
        await unexpected.Response.ShouldBeProblemAsync(StatusCodes.Status500InternalServerError, "order.broken");
        unexpected.Span.ShouldBeRecordedFailure(Unexpected);
        unexpected.Span.Events.Should().NotContain(e => e.Name == "exception", "a returned error has no exception");
    }

    [Fact]
    public async Task ReturnedClientCategoryError_OnlyTagsItsCode()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await StartReturningAsync();

        using var call = await app.GetAsync(recorder, "/not-found");

        await call.Response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
        call.Span.ShouldBeUnset();
        call.Span.ShouldHaveTag(WellKnownTagKeys.ErrorCode, "order.not_found");
        call.Span.ShouldNotHaveTag(WellKnownTagKeys.ErrorType);
        call.Span.Events.Should().NotContain(e => e.Name == "exception");
    }

    [Fact]
    public async Task SuccessfulResult_AndA304_TouchNothing()
    {
        using var recorder = ServerSpans.Record();
        await using var app = await StartReturningAsync();

        using var ok = await app.GetAsync(recorder, "/orders/1");
        using var notModified = await app.SendAsync(
            recorder,
            HttpMethod.Get,
            "/orders/1",
            request => request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"v1\"")));

        ok.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        ok.Span.ShouldBeRecordedSuccess();
        notModified.Response.StatusCode.Should().Be(HttpStatusCode.NotModified);
        notModified.Span.ShouldBeRecordedSuccess();
    }

    private const string ErrorCodesUnexpected = ErrorCodes.Unexpected.Default;

    // The control: the framework's exception middleware with a handler of its own and no suppression callback.
    private static async Task<WebApplication> StartPlainExceptionHandlerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            },
        });
        app.MapGet("/probe", IResult () => throw new ControlProbeException());
        await app.StartAsync();
        return app;
    }

    private static Task<WebApplication> StartReturningAsync() =>
        WebApiTestHost.StartAsync(app =>
        {
            app.MapGet("/unavailable", () => TestErrors.SearchUnreachable.ToErrorResult());
            app.MapGet("/unexpected", () => Unexpected.ToErrorResult());
            app.MapGet("/not-found", () => TestErrors.OrderNotFound.ToErrorResult());
            app.MapGet("/orders/1", () => Result<string>.Success("order").ToOkWithETag(_ => "v1"));
        });

    // A host with the instrumentation at RecordException = true and no exception middleware at all.
    private static async Task<WebApplication> StartUnhandledAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenTelemetry().WithTracing(
            tracing => tracing.AddAspNetCoreInstrumentation(options => options.RecordException = true));
        var app = builder.Build();
        app.MapGet("/probe", IResult () => throw new ControlProbeException());
        await app.StartAsync();
        return app;
    }

    private sealed class TeapotException : Exception;

    private sealed class SuppressionProbeException : Exception;

    private sealed class ControlProbeException : Exception;

    private sealed class TeapotHandler : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            if (exception is not TeapotException)
            {
                return false;
            }

            httpContext.Response.StatusCode = StatusCodes.Status418ImATeapot;
            await httpContext.Response.WriteAsync("handled by the service", cancellationToken);
            return true;
        }
    }

}
