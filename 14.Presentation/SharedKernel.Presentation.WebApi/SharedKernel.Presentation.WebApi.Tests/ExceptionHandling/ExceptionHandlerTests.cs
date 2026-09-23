using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.ExceptionHandling;

/// <summary>
/// Design D1: the exception handler — 499 without a body for a client that went away, server errors logged at Error
/// and client errors at Debug (F8), and no body for a gRPC call.
/// </summary>
public sealed class ExceptionHandlerTests
{
    [Fact]
    public async Task ClientAbort_Is499_WithoutABody_LoggedAtDebug()
    {
        var (handler, logger) = CreateHandler();
        using var aborted = new CancellationTokenSource();
        var context = CreateContext();
        context.RequestAborted = aborted.Token;
        await aborted.CancelAsync();

        var handled = await handler.TryHandleAsync(context, new OperationCanceledException(aborted.Token), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
        context.Response.Body.Length.Should().Be(0);
        logger.Records.Should().ContainSingle().Which.Should().Match<SharedKernel.Testing.Logging.LogRecord>(record =>
            record.EventId.Id == LoggingEventIdRanges.Presentation + 8 && record.LogLevel == LogLevel.Debug);
    }

    [Fact]
    public async Task ServerError_IsLoggedAtError_WithTheException()
    {
        var (handler, logger) = CreateHandler();
        var exception = new InvalidOperationException("boom");

        await handler.TryHandleAsync(CreateContext(), exception, CancellationToken.None);

        var record = logger.Records.Should().ContainSingle().Subject;
        record.EventId.Id.Should().Be(LoggingEventIdRanges.Presentation + 1);
        record.LogLevel.Should().Be(LogLevel.Error);
        record.Exception.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task ClientError_IsLoggedAtDebug()
    {
        var (handler, logger) = CreateHandler();

        await handler.TryHandleAsync(CreateContext(), new NotFoundException(TestErrors.OrderNotFound), CancellationToken.None);

        var record = logger.Records.Should().ContainSingle().Subject;
        record.EventId.Id.Should().Be(LoggingEventIdRanges.Presentation + 7);
        record.LogLevel.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public async Task ServerCategoryDomainException_IsLoggedAtError()
    {
        var (handler, logger) = CreateHandler();

        await handler.TryHandleAsync(CreateContext(), TestErrors.SearchUnreachable.ToException(), CancellationToken.None);

        logger.Records.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Error);
    }

    [Theory]
    [InlineData(StatusCodes.Status413PayloadTooLarge, "request.too_large")]
    [InlineData(StatusCodes.Status400BadRequest, "http.400")]
    public async Task BadHttpRequest_KeepsItsStatusAndMessage(int status, string errorCode)
    {
        var (handler, logger) = CreateHandler();
        var context = CreateContext();

        await handler.TryHandleAsync(context, new BadHttpRequestException("The request was malformed.", status), CancellationToken.None);

        context.Response.StatusCode.Should().Be(status);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        context.Response.Body.Position = 0;
        using var document = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        document.RootElement.GetProperty("errorCode").GetString().Should().Be(errorCode);
        document.RootElement.GetProperty("detail").GetString().Should().Be("The request was malformed.");
        logger.Records.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public async Task GrpcCall_GetsTheStatus_ButNoBody()
    {
        var (handler, _) = CreateHandler();
        var context = CreateContext();
        context.Request.Headers[HeaderNames.ContentType] = "application/grpc";

        var handled = await handler.TryHandleAsync(context, new NotFoundException(TestErrors.OrderNotFound), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.Body.Length.Should().Be(0);
    }

    private static (SharedKernelExceptionHandler Handler, InMemoryLogger<SharedKernelExceptionHandler> Logger) CreateHandler()
    {
        var logger = new InMemoryLogger<SharedKernelExceptionHandler>();
        var handler = new SharedKernelExceptionHandler(
            logger,
            new TestHostEnvironment(),
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));
        return (handler, logger);
    }

    private static DefaultHttpContext CreateContext() => new()
    {
        RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider(),
        Response = { Body = new MemoryStream() },
    };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
