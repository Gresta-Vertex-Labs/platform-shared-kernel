using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
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

    private static async Task<ProblemDetails?> ReadProblemDetailsAsync(HttpContext httpContext)
    {
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<ProblemDetails>(httpContext.Response.Body);
    }
}
