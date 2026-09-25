using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Governance-owned unit tests (not NetArchTest) for <see cref="PipelineOrderAssertion"/> —
/// introduced by WO-036 P-225, T-153.
/// </summary>
public class PipelineOrderAssertionTests
{
    private interface IMarkerRequest;

    private sealed class FirstBehavior<TRequest, TResponse> : SharedKernel.Application.Messaging.IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull, SharedKernel.Application.Messaging.IRequest<TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            SharedKernel.Application.Messaging.RequestHandlerContinuation<TResponse> next,
            CancellationToken cancellationToken) => next();
    }

    private sealed class SecondBehavior<TRequest, TResponse> : SharedKernel.Application.Messaging.IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull, SharedKernel.Application.Messaging.IRequest<TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            SharedKernel.Application.Messaging.RequestHandlerContinuation<TResponse> next,
            CancellationToken cancellationToken) => next();
    }

    private sealed class ThirdBehavior<TRequest, TResponse> : SharedKernel.Application.Messaging.IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull, SharedKernel.Application.Messaging.IRequest<TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            SharedKernel.Application.Messaging.RequestHandlerContinuation<TResponse> next,
            CancellationToken cancellationToken) => next();
    }

    // ---------------------------------------------------------------------------
    // Passing case — registrations in expected order
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-153 (passing case): when the <see cref="IServiceCollection"/> registrations exactly
    /// match the expected order, <see cref="PipelineOrderAssertion.AssertRegistrationOrder"/>
    /// must not throw.
    /// </summary>
    [Fact]
    public void AssertRegistrationOrder_MatchingOrder_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(SharedKernel.Application.Messaging.IPipelineBehavior<,>), typeof(FirstBehavior<,>));
        services.AddTransient(typeof(SharedKernel.Application.Messaging.IPipelineBehavior<,>), typeof(SecondBehavior<,>));
        services.AddTransient(typeof(SharedKernel.Application.Messaging.IPipelineBehavior<,>), typeof(ThirdBehavior<,>));

        var act = () => PipelineOrderAssertion.AssertRegistrationOrder(
            services,
            typeof(FirstBehavior<,>),
            typeof(SecondBehavior<,>),
            typeof(ThirdBehavior<,>));

        act.Should().NotThrow(
            because: "the registration order exactly matches the expected sequence");
    }

    // ---------------------------------------------------------------------------
    // Failing case — registrations out of order
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-153 (failing case): when the <see cref="IServiceCollection"/> registrations are out of
    /// order relative to the expected sequence,
    /// <see cref="PipelineOrderAssertion.AssertRegistrationOrder"/> must throw
    /// <see cref="InvalidOperationException"/> naming both sequences.
    /// </summary>
    [Fact]
    public void AssertRegistrationOrder_OutOfOrder_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(SharedKernel.Application.Messaging.IPipelineBehavior<,>), typeof(SecondBehavior<,>));
        services.AddTransient(typeof(SharedKernel.Application.Messaging.IPipelineBehavior<,>), typeof(FirstBehavior<,>));
        services.AddTransient(typeof(SharedKernel.Application.Messaging.IPipelineBehavior<,>), typeof(ThirdBehavior<,>));

        var act = () => PipelineOrderAssertion.AssertRegistrationOrder(
            services,
            typeof(FirstBehavior<,>),
            typeof(SecondBehavior<,>),
            typeof(ThirdBehavior<,>));

        act.Should()
            .Throw<InvalidOperationException>(
                because: "FirstBehavior and SecondBehavior are registered out of order")
            .WithMessage("*registration order mismatch*");
    }
}
