using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.ExceptionHandling;

public class SharedKernelExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_KnownSharedKernelException_MapsToCarriedErrorStatusCode()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var handler = new SharedKernelExceptionHandler(NullLogger<SharedKernelExceptionHandler>.Instance, environment);
        var httpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new NotFoundException(Error.NotFound("order.not_found", "Order not found."));

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task TryHandleAsync_UnknownException_FallsBackTo500()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var handler = new SharedKernelExceptionHandler(NullLogger<SharedKernelExceptionHandler>.Instance, environment);
        var httpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new InvalidOperationException("boom");

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task TryHandleAsync_UnknownException_OutsideDevelopment_SuppressesDetail()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var handler = new SharedKernelExceptionHandler(NullLogger<SharedKernelExceptionHandler>.Instance, environment);
        var httpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new InvalidOperationException("internal secret detail");

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        var problemDetails = await ReadProblemDetailsAsync(httpContext);
        problemDetails!.Detail.Should().NotContain("internal secret detail");
    }

    [Fact]
    public async Task TryHandleAsync_UnknownException_InDevelopment_DoesNotSuppressKnownErrorDetail()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        var handler = new SharedKernelExceptionHandler(NullLogger<SharedKernelExceptionHandler>.Instance, environment);
        var httpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new ConflictException(Error.Conflict("order.conflict", "Order already shipped."));

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        var problemDetails = await ReadProblemDetailsAsync(httpContext);
        problemDetails!.Detail.Should().Be("Order already shipped.");
    }

    [Fact]
    public async Task TryHandleAsync_ValidationExceptionWithThreeFieldErrors_ProducesMultiErrorBody()
    {
        // (P-402, T-19/T-21) Proves the multi-error branch is actually wired into the handler, not
        // just unit-tested in isolation against ValidationProblemDetailsExtensions directly.
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var handler = new SharedKernelExceptionHandler(NullLogger<SharedKernelExceptionHandler>.Instance, environment);
        var httpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new ValidationException(
        [
            Error.Validation("name.required", "Name is required."),
            Error.Validation("email.invalid", "Email is invalid."),
            Error.Validation("age.range", "Age must be between 0 and 120."),
        ]);

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problemDetails = await ReadProblemDetailsAsync(httpContext);
        var errorsElement = (JsonElement)problemDetails!.Extensions["errors"]!;
        errorsElement.EnumerateObject().Count().Should().Be(3);
    }

    [Theory]
    [MemberData(nameof(NonValidationExceptions))]
    public async Task TryHandleAsync_NonValidationSharedKernelException_ProducesByteForByteIdenticalSingleErrorBody(
        SharedKernelException exception,
        Error expectedError)
    {
        // (P-402, T-20 — REGRESSION) Every non-ValidationException error type must continue
        // producing exactly the single-Error ErrorProblemDetailsExtensions shape — no "errors" key,
        // and every other field identical to calling Error.ToProblemDetails() directly.
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var handler = new SharedKernelExceptionHandler(NullLogger<SharedKernelExceptionHandler>.Instance, environment);
        const string traceIdentifier = "trace-regression";
        var httpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() }, TraceIdentifier = traceIdentifier };
        var expected = expectedError.ToProblemDetails(new DefaultHttpContext { TraceIdentifier = traceIdentifier });

        await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        httpContext.Response.StatusCode.Should().Be(expected.Status);
        var actual = await ReadProblemDetailsAsync(httpContext);
        actual!.Title.Should().Be(expected.Title);
        actual.Detail.Should().Be(expected.Detail);
        actual.Status.Should().Be(expected.Status);
        actual.Type.Should().Be(expected.Type);
        ((JsonElement)actual.Extensions["errorCode"]!).GetString().Should().Be((string)expected.Extensions["errorCode"]!);
        actual.Extensions.Should().NotContainKey("errors");
    }

    public static TheoryData<SharedKernelException, Error> NonValidationExceptions()
    {
        var notFound = Error.NotFound("order.not_found", "Order not found.");
        var conflict = Error.Conflict("order.conflict", "Order already shipped.");
        var forbidden = Error.Forbidden("order.forbidden", "Caller lacks the required approval.");
        var unauthorized = Error.Unauthorized("order.unauthorized", "Caller is not authenticated.");
        var businessRule = Error.BusinessRule("order.rule_violated", "Order total must be positive.");
        var unexpected = Error.Unexpected("order.unexpected", "Something went wrong.");

        return new TheoryData<SharedKernelException, Error>
        {
            { new NotFoundException(notFound), notFound },
            { new ConflictException(conflict), conflict },
            { new UnauthorizedException(forbidden), forbidden },
            { new UnauthorizedException(unauthorized), unauthorized },
            { new DomainException(businessRule), businessRule },
            { new DomainException(unexpected), unexpected },
        };
    }

    private static async Task<ProblemDetails?> ReadProblemDetailsAsync(HttpContext httpContext)
    {
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<ProblemDetails>(httpContext.Response.Body);
    }
}
