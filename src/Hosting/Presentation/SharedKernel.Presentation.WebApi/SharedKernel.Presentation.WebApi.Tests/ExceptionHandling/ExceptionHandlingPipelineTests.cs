using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.ExceptionHandling;

/// <summary>
/// R5 on a real pipeline: the platform's exception handling is the fallback, so an <see cref="IExceptionHandler"/> a
/// service registers after <c>AddSharedKernelWebApi()</c> handles its own exceptions and the platform the rest — each
/// logged once, never again by the exception middleware.
/// </summary>
public sealed class ExceptionHandlingPipelineTests
{
    private const string MiddlewareCategory = "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";

    [Fact]
    public async Task ServiceHandler_RegisteredAfterTheSetup_HandlesItsException()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartAsync(logs);

        using var response = await app.GetTestClient().GetAsync("/teapot");

        response.StatusCode.Should().Be((HttpStatusCode)StatusCodes.Status418ImATeapot);
        (await response.Content.ReadAsStringAsync()).Should().Be("handled by the service");
        PlatformRecords(logs).Should().BeEmpty("the platform never saw the exception");
    }

    [Fact]
    public async Task EveryOtherException_IsStillThePlatformsProblem()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartAsync(logs);

        using var unknown = await app.GetTestClient().GetAsync("/boom");
        using var known = await app.GetTestClient().GetAsync("/missing-order");

        await unknown.ShouldBeProblemAsync(StatusCodes.Status500InternalServerError, ErrorCodes.Unexpected.Default);
        await known.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
    }

    [Fact]
    public async Task PlatformHandledException_IsLoggedOnce_ByThePlatform()
    {
        var logs = new InMemoryLoggerFactory();
        await using var app = await StartAsync(logs);

        using var unknown = await app.GetTestClient().GetAsync("/boom");
        using var known = await app.GetTestClient().GetAsync("/missing-order");

        unknown.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        known.StatusCode.Should().Be(HttpStatusCode.NotFound);
        PlatformRecords(logs).Select(record => record.EventId.Id).Should().BeEquivalentTo(
            [LoggingEventIdRanges.Presentation + 1, LoggingEventIdRanges.Presentation + 7]);
        logs.GetLogger(MiddlewareCategory).Records.Should().NotContain(
            record => record.LogLevel >= LogLevel.Error,
            "the exception middleware's own log is suppressed for what the platform logged");
    }

    [Fact]
    public async Task GrpcRequest_ThatThrowsNotFound_GetsItsStatus_NotTheMiddlewares404Error()
    {
        // A status-only answer starts no response; without AllowStatusCode404Response the middleware would rethrow.
        await using var app = await StartAsync(new InMemoryLoggerFactory());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/missing-order-grpc")
        {
            Content = new ByteArrayContent([]) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/grpc") } },
        };

        using var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    private static IEnumerable<SharedKernel.Testing.Logging.LogRecord> PlatformRecords(InMemoryLoggerFactory logs) =>
        logs.GetLogger(typeof(SharedKernelExceptionHandler).FullName!).Records;

    private static Task<WebApplication> StartAsync(InMemoryLoggerFactory logs) =>
        WebApiTestHost.StartAsync(
            app =>
            {
                app.MapGet("/teapot", IResult () => throw new TeapotException());
                app.MapGet("/boom", IResult () => throw new InvalidOperationException("boom"));
                app.MapGet("/missing-order", IResult () => throw new NotFoundException(TestErrors.OrderNotFound));
                app.MapPost("/missing-order-grpc", IResult () => throw new NotFoundException(TestErrors.OrderNotFound));
            },
            builder => builder.Services.AddExceptionHandler<TeapotHandler>(),
            loggerFactory: logs);

    private sealed class TeapotException : Exception;

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
