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
    /// match the expected order, <see cref="PipelineOrderAssertion.AssertRegistrationOrder(IServiceCollection, Type[])"/>
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
    /// <see cref="PipelineOrderAssertion.AssertRegistrationOrder(IServiceCollection, Type[])"/> must throw
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

    // ---------------------------------------------------------------------------
    // Name-based overload — for the kernel's internal built-in behaviors
    // ---------------------------------------------------------------------------

    [Fact]
    public void AssertRegistrationOrder_ByName_MatchingOrder_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(SharedKernel.Application.Messaging.IPipelineBehavior<,>), typeof(FirstBehavior<,>));
        services.AddTransient(typeof(SharedKernel.Application.Messaging.IPipelineBehavior<,>), typeof(SecondBehavior<,>));

        var act = () => PipelineOrderAssertion.AssertRegistrationOrder(services, "FirstBehavior", "SecondBehavior");

        act.Should().NotThrow();
    }

    [Fact]
    public void AssertRegistrationOrder_ByName_OutOfOrder_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(SharedKernel.Application.Messaging.IPipelineBehavior<,>), typeof(SecondBehavior<,>));
        services.AddTransient(typeof(SharedKernel.Application.Messaging.IPipelineBehavior<,>), typeof(FirstBehavior<,>));

        var act = () => PipelineOrderAssertion.AssertRegistrationOrder(services, "FirstBehavior", "SecondBehavior");

        act.Should().Throw<InvalidOperationException>().WithMessage("*registration order mismatch*");
    }

    /// <summary>
    /// P-579: the real registration puts the built-in behaviors in the canonical order whatever order the
    /// <c>With…()</c> calls are made in. The behaviors are internal, so they are named, not passed as types.
    /// </summary>
    [Fact]
    public void AssertRegistrationOrder_RealRegistration_EveryOptInOutOfOrder_IsCanonical()
    {
        var services = new ServiceCollection();
        SharedKernel.Application.Pipeline.ApplicationServiceCollectionExtensions.AddSharedKernelApplication(
            services,
            typeof(PipelineOrderAssertionTests).Assembly,
            app => SharedKernel.Application.Pipeline.Caching.CachingPipelineExtensions
                .WithCaching(app.WithAuditing().WithTransactions())
                .WithIdempotency());

        var act = () => PipelineOrderAssertion.AssertRegistrationOrder(
            services,
            "TracingBehavior",
            "LoggingBehavior",
            "MetricsBehavior",
            "AuthorizationBehavior",
            "ValidationBehavior",
            "CachingBehavior",
            "CommandScopeBehavior",
            "IdempotencyBehavior",
            "AuditingBehavior",
            "TransactionBehavior",
            "AuditingCommitBehavior",
            "CacheInvalidationBehavior");

        act.Should().NotThrow(because: "AddSharedKernelApplication registers the pipeline in its canonical order");
    }
}
