using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Polly.Registry;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Behaviors.Caching;
using SharedKernel.Application.Behaviors.DualApproval;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Application.Behaviors.Metrics;
using SharedKernel.Application.Behaviors.Resilience;
using SharedKernel.Application.Behaviors.Tracing;
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

    // ---- WO-058, T-71: AddDualApprovalBehavior() two-dependency Build()-time guard ----

    [Fact]
    public void Build_DualApprovalBehaviorWithoutIAuthorizationContext_ThrowsInvalidOperationExceptionNamingIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IDualApprovalStore>());

        var act = () => services.AddSharedKernelApplicationBehaviors().AddDualApprovalBehavior().Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IAuthorizationContext*");
    }

    [Fact]
    public void Build_DualApprovalBehaviorWithoutIDualApprovalStore_ThrowsInvalidOperationExceptionNamingIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IAuthorizationContext>());

        var act = () => services.AddSharedKernelApplicationBehaviors().AddDualApprovalBehavior().Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IDualApprovalStore*");
    }

    [Fact]
    public void Build_DualApprovalBehaviorWithBothDependenciesMissing_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddDualApprovalBehavior().Build();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Build_DualApprovalBehaviorWithBothDependenciesRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IAuthorizationContext>());
        services.AddSingleton(Substitute.For<IDualApprovalStore>());

        var act = () => services.AddSharedKernelApplicationBehaviors().AddDualApprovalBehavior().Build();

        act.Should().NotThrow();
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
    public void Build_ResilienceBehaviorWithoutResiliencePipelineProvider_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddResilienceBehavior().Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ResiliencePipelineProvider*");
    }

    [Fact]
    public void Build_CacheInvalidationBehaviorWithoutICacheService_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddCacheInvalidationBehavior().Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ICacheService*");
    }

    [Fact]
    public void Build_CacheInvalidationBehaviorWithICacheServiceRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICacheService>());

        var act = () => services.AddSharedKernelApplicationBehaviors().AddCacheInvalidationBehavior().Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_ResilienceBehaviorWithProviderRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ResiliencePipelineProvider<string>>());

        var act = () => services.AddSharedKernelApplicationBehaviors().AddResilienceBehavior().Build();

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
        services.AddSingleton(Substitute.For<ResiliencePipelineProvider<string>>());

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
            typeof(TracingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(CachingBehavior<,>),
            typeof(ResilienceBehavior<,>),
            typeof(IdempotentCommandBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(CacheInvalidationBehavior<,>));
    }

    public static TheoryData<Action<ApplicationBehaviorsBuilder>> AllCallOrderPermutations()
    {
        return new TheoryData<Action<ApplicationBehaviorsBuilder>>
        {
            b => b.AddLoggingBehavior().AddMetricsBehavior().AddTracingBehavior().AddValidationBehavior()
                  .AddAuthorizationBehavior().AddCachingBehavior().AddResilienceBehavior()
                  .AddIdempotencyBehavior().AddTransactionBehavior().AddCacheInvalidationBehavior(),
            b => b.AddCacheInvalidationBehavior().AddTransactionBehavior().AddIdempotencyBehavior()
                  .AddResilienceBehavior().AddCachingBehavior().AddAuthorizationBehavior()
                  .AddValidationBehavior().AddTracingBehavior().AddMetricsBehavior().AddLoggingBehavior(),
            b => b.AddCachingBehavior().AddLoggingBehavior().AddTransactionBehavior().AddTracingBehavior()
                  .AddValidationBehavior().AddIdempotencyBehavior().AddCacheInvalidationBehavior()
                  .AddMetricsBehavior().AddAuthorizationBehavior().AddResilienceBehavior(),
        };
    }

    // ---- WO-039, P-243 (T-50/T-51/T-52): AddDefaultBehaviors() onboarding preset ----

    [Fact]
    public void Build_AddDefaultBehaviors_RegistersSameSetAsFourIndividualCalls_InCanonicalOrder()
    {
        var presetServices = new ServiceCollection();
        presetServices.AddSharedKernelApplicationBehaviors().AddDefaultBehaviors().Build();

        var individualServices = new ServiceCollection();
        individualServices.AddSharedKernelApplicationBehaviors()
            .AddLoggingBehavior()
            .AddMetricsBehavior()
            .AddTracingBehavior()
            .AddValidationBehavior()
            .Build();

        var presetTypes = presetServices
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType!.GetGenericTypeDefinition())
            .ToList();

        var individualTypes = individualServices
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType!.GetGenericTypeDefinition())
            .ToList();

        presetTypes.Should().Equal(individualTypes,
            "AddDefaultBehaviors() must be provably equivalent to calling the four individual " +
            ".AddXBehavior() methods — no reimplementation, no divergent registration logic");

        presetTypes.Should().Equal(
            typeof(LoggingBehavior<,>),
            typeof(MetricsBehavior<,>),
            typeof(TracingBehavior<,>),
            typeof(ValidationBehavior<,>));
    }

    [Fact]
    public void Build_AddDefaultBehaviors_CombinedWithIndividualLoggingCall_ProducesNoDuplicateRegistration()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelApplicationBehaviors()
            .AddDefaultBehaviors()
            .AddLoggingBehavior()
            .Build();

        var registeredTypes = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType!.GetGenericTypeDefinition())
            .ToList();

        registeredTypes.Should().Equal(
            [
                typeof(LoggingBehavior<,>),
                typeof(MetricsBehavior<,>),
                typeof(TracingBehavior<,>),
                typeof(ValidationBehavior<,>)
            ],
            "combining the preset with a redundant individual .AddLoggingBehavior() call must not " +
            "double-register LoggingBehavior<,> or disturb the fixed canonical order");
    }

    [Fact]
    public void Build_AddDefaultBehaviorsAlone_NeverThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddDefaultBehaviors().Build();

        act.Should().NotThrow(
            "none of Logging/Metrics/Tracing/Validation carries a Build()-time missing-dependency guard, " +
            "so the zero-prerequisite preset must never throw InvalidOperationException on its own");
    }
}
