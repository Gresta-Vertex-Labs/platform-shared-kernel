using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Filters;

public class HubExceptionMappingFilterTests
{
    [Fact]
    public async Task InvokeMethodAsync_NoException_ReturnsNextResult()
    {
        var filter = new HubExceptionMappingFilter(NullLogger<HubExceptionMappingFilter>.Instance);
        var invocationContext = CreateInvocationContext();

        var result = await filter.InvokeMethodAsync(invocationContext, _ => ValueTask.FromResult<object?>("ok"));

        result.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeMethodAsync_KnownSharedKernelException_RethrowsAsHubExceptionWithErrorMessage()
    {
        var filter = new HubExceptionMappingFilter(NullLogger<HubExceptionMappingFilter>.Instance);
        var invocationContext = CreateInvocationContext();
        var error = Error.NotFound("order.not_found", "Order could not be found.");

        var act = async () => await filter.InvokeMethodAsync(
            invocationContext,
            _ => throw new NotFoundException(error));

        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be("Order could not be found.");
    }

    [Fact]
    public async Task InvokeMethodAsync_UnknownException_RethrowsAsGenericRedactedHubException()
    {
        var filter = new HubExceptionMappingFilter(NullLogger<HubExceptionMappingFilter>.Instance);
        var invocationContext = CreateInvocationContext();

        var act = async () => await filter.InvokeMethodAsync(
            invocationContext,
            _ => throw new InvalidOperationException("internal secret stack detail"));

        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().NotContain("internal secret stack detail");
        exception.Which.Message.Should().Be("An unexpected error occurred.");
    }

    [Fact]
    public async Task InvokeMethodAsync_AlreadyHubException_PassesThroughUnchanged()
    {
        var filter = new HubExceptionMappingFilter(NullLogger<HubExceptionMappingFilter>.Instance);
        var invocationContext = CreateInvocationContext();
        var originalException = new HubException("Already safe.");

        var act = async () => await filter.InvokeMethodAsync(
            invocationContext,
            _ => throw originalException);

        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Should().BeSameAs(originalException);
    }

    private static HubInvocationContext CreateInvocationContext()
    {
        var hubCallerContext = Substitute.For<HubCallerContext>();
        var hub = Substitute.For<Hub>();
        var methodInfo = typeof(HubExceptionMappingFilterTests).GetMethod(
            nameof(CreateInvocationContext),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        return new HubInvocationContext(
            hubCallerContext,
            Substitute.For<IServiceProvider>(),
            hub,
            methodInfo,
            Array.Empty<object>());
    }
}
