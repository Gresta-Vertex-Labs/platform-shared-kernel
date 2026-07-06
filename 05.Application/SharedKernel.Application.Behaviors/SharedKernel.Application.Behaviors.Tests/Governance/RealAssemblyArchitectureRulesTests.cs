using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Polly.Registry;
using SharedKernel.ArchitectureTests;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Behaviors.Caching;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Application.Behaviors.Metrics;
using SharedKernel.Application.Behaviors.Resilience;
using SharedKernel.Application.Behaviors.Tracing;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Behaviors.Validation;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Tests.Governance;

/// <summary>
/// Invokes four already-built <c>00.Governance</c> architecture-test rule groups against the real,
/// compiled <c>SharedKernel.Application.Behaviors</c> assembly (WO-039 P-241 gap-closure).
/// </summary>
/// <remarks>
/// <para>
/// T-41: <see cref="ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure"/>,
/// <see cref="ApplicationPipelineRules.NoExistingBehaviorMatchesStreamRequestConstraint"/>, and
/// <see cref="ApplicationPipelineRules.NoHandRolledRetryLoopOutsideResilienceBehavior"/> are all
/// independent of the <c>00.Governance</c> <c>SK.00.DomainEventDispatcherReflectionExemption</c>
/// blocker (P-240) — none of them inspect <c>MakeGenericMethod</c> call sites — so they are safe to
/// run against the real assembly today.
/// </para>
/// <para>
/// T-42: <see cref="PipelineOrderAssertion.AssertRegistrationOrder"/> is exercised against the
/// actual <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/> produced by
/// <see cref="ApplicationBehaviorsBuilder.Build"/> with every unary behavior opted in, pinning the
/// documented ten-step canonical order as a permanent regression guard.
/// </para>
/// <para>
/// T-43: <see cref="MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag"/> is
/// expected to PASS now that P-239 shipped the <c>"outcome"</c> tag on
/// <see cref="MetricsBehavior{TRequest,TResponse}"/>'s <c>RequestDuration.Record(...)</c> call site.
/// </para>
/// <para>
/// T-40 (<see cref="ReflectionGuardRules"/>.<c>NoMakeGenericMethodReflection</c> against both real
/// assemblies) is deliberately NOT exercised here — it remains genuinely blocked on
/// <c>00.Governance</c> P-240 (<c>ReflectionExemptionRegistry</c> entry for
/// <c>MediatRDomainEventDispatcher</c>), which is still 0/5 tasks as of this session. See
/// <c>05.Application/state-map.md</c> Phase <c>SK.05.Tests</c> for the tracked blocker.
/// </para>
/// </remarks>
public sealed class RealAssemblyArchitectureRulesTests
{
    private static readonly System.Reflection.Assembly BehaviorsAssembly =
        typeof(AuthorizationBehavior<,>).Assembly;

    // ---- T-41 ----

    [Fact]
    public void BehaviorsNeverReferenceConcreteInfrastructure_RealAssembly_Passes()
    {
        var result = ApplicationPipelineRules
            .BehaviorsNeverReferenceConcreteInfrastructure(BehaviorsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "TracingBehavior/ResilienceBehavior/CacheInvalidationBehavior must reference only "
            + "SharedKernel.Caching.Abstractions (never a concrete caching/persistence/messaging "
            + "provider package). Failing types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void NoExistingBehaviorMatchesStreamRequestConstraint_RealAssembly_Passes()
    {
        var result = ApplicationPipelineRules
            .NoExistingBehaviorMatchesStreamRequestConstraint(BehaviorsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "no unary IPipelineBehavior<,> implementor's TRequest constraint may structurally "
            + "satisfy MediatR's IStreamRequest<TResponse>. Failing types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void NoHandRolledRetryLoopOutsideResilienceBehavior_RealAssembly_Passes()
    {
        var result = ApplicationPipelineRules
            .NoHandRolledRetryLoopOutsideResilienceBehavior(BehaviorsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "no type other than ResilienceBehavior may call Task.Delay. Failing types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- T-42 ----

    [Fact]
    public void AssertRegistrationOrder_AllUnaryBehaviorsAdded_MatchesCanonicalTenStepOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IUnitOfWork>());
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddSingleton(Substitute.For<IAuthorizationContext>());
        services.AddSingleton(Substitute.For<IIdempotencyKeyStore>());
        services.AddSingleton(Substitute.For<ResiliencePipelineProvider<string>>());

        services.AddSharedKernelApplicationBehaviors()
            .AddLoggingBehavior()
            .AddMetricsBehavior()
            .AddTracingBehavior()
            .AddValidationBehavior()
            .AddAuthorizationBehavior()
            .AddCachingBehavior()
            .AddResilienceBehavior()
            .AddIdempotencyBehavior()
            .AddTransactionBehavior()
            .AddCacheInvalidationBehavior()
            .Build();

        var act = () => PipelineOrderAssertion.AssertRegistrationOrder(
            services,
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

        act.Should().NotThrow(
            "ApplicationBehaviorsBuilder.Build() must register all ten unary behaviors in the "
            + "fixed canonical order: Logging -> Metrics -> Tracing -> Validation -> Authorization "
            + "-> Caching -> Resilience -> Idempotency -> Transaction -> CacheInvalidation");
    }

    [Fact]
    public void AssertRegistrationOrder_WrongOrderExpectation_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IUnitOfWork>());

        services.AddSharedKernelApplicationBehaviors()
            .AddLoggingBehavior()
            .AddTransactionBehavior()
            .Build();

        var act = () => PipelineOrderAssertion.AssertRegistrationOrder(
            services,
            typeof(TransactionBehavior<,>),
            typeof(LoggingBehavior<,>));

        act.Should().Throw<InvalidOperationException>(
            "the helper itself must detect and report an order mismatch, proving it is load-bearing "
            + "rather than a vacuous pass");
    }

    // ---- T-43 ----

    [Fact]
    public void RequestDurationRecordsIncludeOutcomeTag_RealAssembly_Passes()
    {
        var result = MetricsInstrumentationRules
            .RequestDurationRecordsIncludeOutcomeTag(BehaviorsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "every Histogram<double>.Record(...) call site in SharedKernel.Application.Behaviors "
            + "must carry an \"outcome\" tag literal, now that P-239 retrofitted MetricsBehavior<,>. "
            + "Failing types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
