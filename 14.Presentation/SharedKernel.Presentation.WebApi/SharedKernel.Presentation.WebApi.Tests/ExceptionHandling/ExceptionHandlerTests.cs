using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.ExceptionHandling;

/// <summary>
/// Design D1 and R5/R10: the platform's exception handling — the fallback of <c>UseExceptionHandler()</c>, 499
/// without a body for any exception once the client went away, 504 for timeouts inside the service, server errors
/// logged at Error and client errors at Debug (F8), and no body for a gRPC call.
/// </summary>
public sealed class ExceptionHandlerTests
{
    [Theory]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(TimeoutException))]
    public async Task R10_AnyException_WhileTheClientIsGone_Is499_WithoutABody_LoggedAtDebug(Type exceptionType)
    {
        var (handler, logger) = CreateHandler();
        using var aborted = new CancellationTokenSource();
        var context = CreateContext();
        context.RequestAborted = aborted.Token;
        await aborted.CancelAsync();

        await handler.HandleAsync(context, (Exception)Activator.CreateInstance(exceptionType)!);

        context.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);
        context.Response.Body.Length.Should().Be(0);
        logger.Records.Should().ContainSingle().Which.Should().Match<SharedKernel.Testing.Logging.LogRecord>(record =>
            record.EventId.Id == LoggingEventIdRanges.Presentation + 8 && record.LogLevel == LogLevel.Debug);
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task R10_TimeoutInsideTheService_Is504_TimeoutDefault_LoggedAtError(Type exceptionType)
    {
        var (handler, logger) = CreateHandler();
        var context = CreateContext();

        await handler.HandleAsync(context, (Exception)Activator.CreateInstance(exceptionType)!);

        context.Response.StatusCode.Should().Be(StatusCodes.Status504GatewayTimeout);
        var body = await ReadBodyAsync(context);
        body.GetProperty("errorCode").GetString().Should().Be(ErrorCodes.Timeout.Default);
        body.GetProperty("detail").GetString().Should().Be("The operation did not complete in time.");
        logger.Records.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Error);
    }

    [Fact]
    public async Task ServerError_IsLoggedAtError_WithTheException()
    {
        var (handler, logger) = CreateHandler();
        var exception = new InvalidOperationException("boom");

        await handler.HandleAsync(CreateContext(), exception);

        var record = logger.Records.Should().ContainSingle().Subject;
        record.EventId.Id.Should().Be(LoggingEventIdRanges.Presentation + 1);
        record.LogLevel.Should().Be(LogLevel.Error);
        record.Exception.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task ClientError_IsLoggedAtDebug()
    {
        var (handler, logger) = CreateHandler();

        await handler.HandleAsync(CreateContext(), new NotFoundException(TestErrors.OrderNotFound));

        var record = logger.Records.Should().ContainSingle().Subject;
        record.EventId.Id.Should().Be(LoggingEventIdRanges.Presentation + 7);
        record.LogLevel.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public async Task ServerCategoryDomainException_IsLoggedAtError()
    {
        var (handler, logger) = CreateHandler();

        await handler.HandleAsync(CreateContext(), TestErrors.SearchUnreachable.ToException());

        logger.Records.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Error);
    }

    [Theory]
    [InlineData(StatusCodes.Status413PayloadTooLarge, "request.too_large", "Request body too large.")]
    [InlineData(StatusCodes.Status415UnsupportedMediaType, "http.415", "Expected a supported JSON media type.")]
    public async Task BadHttpRequest_OtherThan400_KeepsItsStatusAndMessage(int status, string errorCode, string message)
    {
        var (handler, logger) = CreateHandler();
        var context = CreateContext();

        await handler.HandleAsync(context, new BadHttpRequestException(message, status));

        context.Response.StatusCode.Should().Be(status);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        var body = await ReadBodyAsync(context);
        body.GetProperty("errorCode").GetString().Should().Be(errorCode);
        body.GetProperty("detail").GetString().Should().Be(message);
        logger.Records.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public async Task R9_BadHttpRequest400_IsAValidationProblem_WithoutTheFrameworksMessage()
    {
        // The framework's message names the parameter and its .NET type: "Failed to bind parameter "int id" from "x"".
        var (handler, _) = CreateHandler();
        var context = CreateContext();

        await handler.HandleAsync(context, new BadHttpRequestException("Failed to bind parameter \"System.Int32 id\" from \"x\"."));

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var body = await ReadBodyAsync(context);
        body.GetProperty("errorCode").GetString().Should().Be(ErrorCodes.Validation.InvalidFormat);
        body.GetProperty("detail").GetString().Should().Be("The request is not valid.");
        body.GetRawText().Should().NotContain("System.Int32").And.NotContain("Failed to bind");
    }

    [Fact]
    public async Task R9_BadHttpRequest400_FromAJsonBody_NamesTheField_ByItsJsonPath()
    {
        var (handler, _) = CreateHandler();
        var context = CreateContext();
        var json = new JsonException("The JSON value could not be converted to System.Int32.", "$.customer.age", 0, 12);

        await handler.HandleAsync(context, new BadHttpRequestException("Failed to read parameter \"Payload payload\" from the request body as JSON.", json));

        var body = await ReadBodyAsync(context);
        body.GetProperty("errorCode").GetString().Should().Be(ErrorCodes.Validation.Failed);
        body.GetProperty("errors").GetProperty("customer.age")[0].GetString().Should().Be("The value is not valid.");
        body.GetProperty("errorCodes").GetProperty("customer.age")[0].GetString().Should().Be(ErrorCodes.Validation.InvalidFormat);
        body.GetRawText().Should().NotContain("System.").And.NotContain("Payload");
    }

    [Fact]
    public async Task GrpcCall_GetsTheStatus_ButNoBody()
    {
        var (handler, _) = CreateHandler();
        var context = CreateContext();
        context.Request.Headers[HeaderNames.ContentType] = "application/grpc";

        await handler.HandleAsync(context, new NotFoundException(TestErrors.OrderNotFound));

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ReadsTheExceptionTheMiddlewareRecorded()
    {
        var (handler, _) = CreateHandler();
        var context = CreateContext();
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature { Error = new NotFoundException(TestErrors.OrderNotFound) });

        await handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task R5_Install_BecomesTheFallback_AndSuppressesTheMiddlewaresLog_ForWhatItHandled()
    {
        var (handler, _) = CreateHandler();
        var options = new ExceptionHandlerOptions();

        handler.Install(options);

        options.ExceptionHandler.Should().NotBeNull();
        options.AllowStatusCode404Response.Should().BeTrue("a NotFoundException is a legitimate 404");
        var handledHere = CreateContext();
        await handler.HandleAsync(handledHere, new NotFoundException(TestErrors.OrderNotFound));

        Suppressed(options, handledHere, ExceptionHandledType.ExceptionHandlerDelegate).Should().BeTrue();
        Suppressed(options, handledHere, ExceptionHandledType.Unhandled).Should().BeTrue("a gRPC status or a 499 starts no response");
        Suppressed(options, CreateContext(), ExceptionHandledType.ExceptionHandlerService)
            .Should().BeTrue("the framework's own default for a service's IExceptionHandler is kept");
        Suppressed(options, CreateContext(), ExceptionHandledType.Unhandled).Should().BeFalse();
    }

    [Fact]
    public void R5_Install_KeepsAServicesOwnHandler_Path_AndSuppressionCallback()
    {
        var (handler, _) = CreateHandler();
        RequestDelegate own = _ => Task.CompletedTask;
        var withHandler = new ExceptionHandlerOptions { ExceptionHandler = own };
        var withPath = new ExceptionHandlerOptions { ExceptionHandlingPath = "/error" };
        var withCallback = new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true };

        handler.Install(withHandler);
        handler.Install(withPath);
        handler.Install(withCallback);

        withHandler.ExceptionHandler.Should().BeSameAs(own);
        withPath.ExceptionHandler.Should().BeNull("the middleware re-executes the service's error path");
        Suppressed(withCallback, CreateContext(), ExceptionHandledType.Unhandled).Should().BeTrue("the service's callback still decides");
    }

    private static bool Suppressed(ExceptionHandlerOptions options, HttpContext context, ExceptionHandledType handledBy) =>
        options.SuppressDiagnosticsCallback!(new ExceptionHandlerSuppressDiagnosticsContext
        {
            HttpContext = context,
            Exception = new InvalidOperationException(),
            ExceptionHandledBy = handledBy,
        });

    private static (SharedKernelExceptionHandler Handler, InMemoryLogger<SharedKernelExceptionHandler> Logger) CreateHandler()
    {
        var logger = new InMemoryLogger<SharedKernelExceptionHandler>();
        var handler = new SharedKernelExceptionHandler(
            logger,
            new TestHostEnvironment(),
            Microsoft.Extensions.Options.Options.Create(new SharedKernelWebApiOptions()));
        return (handler, logger);
    }

    private static DefaultHttpContext CreateContext() => new()
    {
        RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider(),
        Response = { Body = new MemoryStream() },
    };

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
