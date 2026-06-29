using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.Caching;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Application.Behaviors.Metrics;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Behaviors.Validation;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Tests.Extensions;

/// <summary>
/// Verifies <see cref="ApplicationBehaviorsBuilder"/>'s missing-dependency guards and the fixed
/// canonical registration order, independent of <c>.AddXBehavior()</c> call order.
/// </summary>
public sealed class ApplicationBehaviorsBuilderTests
{
    [Fact]
    public void Build_TransactionBehaviorWithoutIUnitOfWork_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddTransactionBehavior().Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IUnitOfWork*");
    }

    [Fact]
    public void Build_CachingBehaviorWithoutICacheService_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddCachingBehavior().Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ICacheService*");
    }

    [Fact]
    public void Build_AuthorizationBehaviorWithoutIAuthorizationContext_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddAuthorizationBehavior().Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IAuthorizationContext*");
    }

    [Fact]
    public void Build_IdempotencyBehaviorWithoutIIdempotencyKeyStore_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddIdempotencyBehavior().Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IIdempotencyKeyStore*");
    }

    [Fact]
    public void Build_TransactionBehaviorWithIUnitOfWorkRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IUnitOfWork>());

        var act = () => services.AddSharedKernelApplicationBehaviors().AddTransactionBehavior().Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_CachingBehaviorWithICacheServiceRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICacheService>());

        var act = () => services.AddSharedKernelApplicationBehaviors().AddCachingBehavior().Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_NeverCallsAddMediatR()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IUnitOfWork>());

        services.AddSharedKernelApplicationBehaviors()
            .AddLoggingBehavior()
            .AddTransactionBehavior()
            .Build();

        services.Any(d => d.ServiceType == typeof(IMediator)).Should().BeFalse();
        services.Any(d => d.ServiceType == typeof(ISender)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(AllCallOrderPermutations))]
    public void Build_RegistersBehaviorsInFixedCanonicalOrder_RegardlessOfCallOrder(
        Action<ApplicationBehaviorsBuilder> configureInOrder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IUnitOfWork>());
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddSingleton(Substitute.For<IAuthorizationContext>());
        services.AddSingleton(Substitute.For<IIdempotencyKeyStore>());

        var builder = services.AddSharedKernelApplicationBehaviors();
        configureInOrder(builder);
        builder.Build();

        var registeredBehaviorTypes = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType!.GetGenericTypeDefinition())
            .ToList();

        registeredBehaviorTypes.Should().Equal(
            typeof(LoggingBehavior<,>),
            typeof(MetricsBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(CachingBehavior<,>),
            typeof(IdempotentCommandBehavior<,>),
            typeof(TransactionBehavior<,>));
    }

    public static TheoryData<Action<ApplicationBehaviorsBuilder>> AllCallOrderPermutations()
    {
        return new TheoryData<Action<ApplicationBehaviorsBuilder>>
        {
            b => b.AddLoggingBehavior().AddMetricsBehavior().AddValidationBehavior()
                  .AddAuthorizationBehavior().AddCachingBehavior().AddIdempotencyBehavior().AddTransactionBehavior(),
            b => b.AddTransactionBehavior().AddIdempotencyBehavior().AddCachingBehavior()
                  .AddAuthorizationBehavior().AddValidationBehavior().AddMetricsBehavior().AddLoggingBehavior(),
            b => b.AddCachingBehavior().AddLoggingBehavior().AddTransactionBehavior()
                  .AddValidationBehavior().AddIdempotencyBehavior().AddMetricsBehavior().AddAuthorizationBehavior(),
        };
    }
}
