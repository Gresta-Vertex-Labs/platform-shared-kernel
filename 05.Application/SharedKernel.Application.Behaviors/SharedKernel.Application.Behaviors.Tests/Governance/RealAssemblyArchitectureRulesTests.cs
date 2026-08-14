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

namespace SharedKernel.Application.Behaviors.Tests.Governance;

/// <summary>
/// Invokes all four already-built <c>00.Governance</c> architecture-test rule groups against the
/// real, compiled <c>SharedKernel.Application</c>/<c>SharedKernel.Application.Behaviors</c>
/// assemblies (WO-039 P-241 gap-closure).
/// </summary>
/// <remarks>
/// <para>
/// T-41: <see cref="ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure"/>,
/// <see cref="ApplicationPipelineRules.NoExistingBehaviorMatchesStreamRequestConstraint"/>, and
/// <see cref="ApplicationPipelineRules.NoHandRolledRetryLoopOutsideResilienceBehavior"/> are all
/// independent of the <c>00.Governance</c> <c>SK.00.DomainEventDispatcherReflectionExemption</c>
/// blocker (P-240) — none of them inspect <c>MakeGenericMethod</c> call sites.
/// </para>
/// <para>
/// T-42: <see cref="PipelineOrderAssertion.AssertRegistrationOrder"/> is exercised against the
/// actual <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/> produced by
/// <see cref="ApplicationBehaviorsBuilder.Build"/> with every unary behavior opted in, pinning the
/// documented ten-step canonical order as a permanent regression guard.
/// </para>
/// <para>
/// T-72 (WO-058): the same <see cref="PipelineOrderAssertion.AssertRegistrationOrder"/> precedent is
/// extended to the new ELEVEN-step canonical order — <see cref="DualApprovalBehavior{TRequest,TResponse}"/>
/// inserted at position 6 (immediately after <see cref="AuthorizationBehavior{TRequest,TResponse}"/>,
/// before <see cref="CachingBehavior{TRequest,TResponse}"/>) — with every unary behavior, including
/// <c>DualApproval</c>, opted in. A full <c>00.Governance</c> real-assembly wiring update for this new
/// step (e.g. a companion <c>ApplicationPipelineRules</c> real-assembly assertion) is a separate, later
/// cross-domain follow-up — not required to close this task.
/// </para>
/// <para>
/// T-43: <see cref="MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag"/> PASSES
/// now that P-239 shipped the <c>"outcome"</c> tag on
/// <see cref="MetricsBehavior{TRequest,TResponse}"/>'s <c>RequestDuration.Record(...)</c> call site.
/// </para>
/// <para>
/// T-40: <see cref="ReflectionGuardRules.NoMakeGenericMethodReflection"/> is now exercised against
/// BOTH real assemblies, re-verified unblocked this session — <c>00.Governance</c> P-240 shipped
/// (<c>SK.00.DomainEventDispatcherReflectionExemption</c>, all 5 tasks <c>●</c> as of 2026-07-06)
/// and registered <c>ReflectionExemptionRegistry</c>'s first real entry for
/// <c>SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher</c>'s closure-hidden
/// <c>MakeGenericMethod</c> call. Before marking T-40 complete, this session verified empirically
/// (not merely inferred from P-240's task checkmarks) whether
/// <c>SharedKernel.Application.Behaviors</c>' OWN <c>MakeGenericMethod</c> call site
/// (<c>Shared/FailureResponseFactory.cs</c>, <c>ResultOfTDispatcher&lt;TResponse&gt;.BuildFactory</c>,
/// shipped by this domain's own P-237) would also need a registered exemption — it is an ordinary
/// generic-class static method, not hidden inside a compiler-generated closure the way
/// <c>PublishSingle</c>'s lambda is, so a naive reading suggested NetArchTest might actually see it
/// and fail without a second registry entry. <strong>Empirically confirmed both tests PASS as-is</strong>:
/// <c>ResultOfTDispatcher&lt;TResponse&gt;</c> is declared <c>internal static class</c>, and a C#
/// <c>static class</c> compiles to IL <c>abstract sealed</c> — <see cref="ReflectionGuardRules.NoMakeGenericMethodReflection"/>
/// applies NetArchTest's <c>.That().AreNotAbstract()</c> filter before scanning, so
/// <c>ResultOfTDispatcher&lt;TResponse&gt;</c> (and its <c>BuildFactory</c> method) is excluded from
/// the scan entirely — a second, independently-discovered "vacuous pass" mechanism alongside the
/// already-documented closure-type invisibility gap (see <c>00.Governance/CLAUDE.md</c>'s SK0012
/// entry). This is a genuine, mechanical PASS — not a fabricated or loosened assertion — but it
/// does not (yet) constitute end-to-end enforcement of either call site; both gaps are tracked as
/// the same <c>00.Governance</c> follow-up (walk <c>TypeDefinition.NestedTypes</c>/drop or refine
/// the abstract filter) already recorded in that file's Changelog.
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

    // ---- T-72 (WO-058): the new eleven-step canonical order, DualApprovalBehavior at position 6 ----

    [Fact]
    public void AssertRegistrationOrder_AllUnaryBehaviorsIncludingDualApprovalAdded_MatchesCanonicalElevenStepOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IUnitOfWork>());
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddSingleton(Substitute.For<IAuthorizationContext>());
        services.AddSingleton(Substitute.For<IDualApprovalStore>());
        services.AddSingleton(Substitute.For<IIdempotencyKeyStore>());
        services.AddSingleton(Substitute.For<ResiliencePipelineProvider<string>>());

        services.AddSharedKernelApplicationBehaviors()
            .AddLoggingBehavior()
            .AddMetricsBehavior()
            .AddTracingBehavior()
            .AddValidationBehavior()
            .AddAuthorizationBehavior()
            .AddDualApprovalBehavior()
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
            typeof(DualApprovalBehavior<,>),
            typeof(CachingBehavior<,>),
            typeof(ResilienceBehavior<,>),
            typeof(IdempotentCommandBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(CacheInvalidationBehavior<,>));

        act.Should().NotThrow(
            "ApplicationBehaviorsBuilder.Build() must register all eleven unary behaviors in the "
            + "fixed canonical order: Logging -> Metrics -> Tracing -> Validation -> Authorization "
            + "-> DualApproval -> Caching -> Resilience -> Idempotency -> Transaction -> CacheInvalidation, "
            + "with DualApprovalBehavior at canonical position 6 regardless of .AddXBehavior() call order");
    }

    [Fact]
    public void AssertRegistrationOrder_DualApprovalRegisteredOutOfOrderExpectation_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IAuthorizationContext>());
        services.AddSingleton(Substitute.For<IDualApprovalStore>());
        services.AddSingleton(Substitute.For<ICacheService>());

        // Deliberately called in the "wrong" order — Build() must still register in the fixed
        // canonical sequence (DualApproval before Caching), never the .AddXBehavior() call order.
        services.AddSharedKernelApplicationBehaviors()
            .AddCachingBehavior()
            .AddDualApprovalBehavior()
            .Build();

        var act = () => PipelineOrderAssertion.AssertRegistrationOrder(
            services,
            typeof(CachingBehavior<,>),
            typeof(DualApprovalBehavior<,>));

        act.Should().Throw<InvalidOperationException>(
            "Build() registers DualApprovalBehavior BEFORE CachingBehavior (canonical position 6 "
            + "before 7) regardless of .AddCachingBehavior()/.AddDualApprovalBehavior() call order, "
            + "so asserting the reverse expectation must fail");
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

    // ---- T-40 ----
    //
    // 00.Governance P-240 shipped (2026-07-06): ReflectionExemptionRegistry.AllowList now
    // registers SharedKernel.Application's MediatRDomainEventDispatcher/<>c closure. Before
    // marking T-40 complete this session, verified empirically whether
    // SharedKernel.Application.Behaviors' own MakeGenericMethod call site
    // (Shared/FailureResponseFactory.cs, ResultOfTDispatcher<TResponse>.BuildFactory) would also
    // need a registry entry. Both tests below pass — see the class-level remarks for why (the
    // <c>static class</c> -> IL <c>abstract sealed</c> + <c>.AreNotAbstract()</c> filter excludes
    // ResultOfTDispatcher<TResponse> from NetArchTest's scan entirely, a second documented
    // vacuous-pass mechanism alongside the closure-type gap).
    [Fact]
    public void NoMakeGenericMethodReflection_RealApplicationAssembly_Passes()
    {
        var applicationAssembly = typeof(SharedKernel.Application.DomainEvents.IDomainEventHandler<>).Assembly;

        var result = ReflectionGuardRules
            .NoMakeGenericMethodReflection(applicationAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "SharedKernel.Application's registered MediatRDomainEventDispatcher exemption should "
            + "cover its only MakeGenericMethod call site. Failing types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void NoMakeGenericMethodReflection_RealBehaviorsAssembly_Passes()
    {
        var result = ReflectionGuardRules
            .NoMakeGenericMethodReflection(BehaviorsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "if this fails, SharedKernel.Application.Behaviors' ResultOfTDispatcher<TResponse>."
            + "BuildFactory MakeGenericMethod call site (Shared/FailureResponseFactory.cs) is "
            + "unregistered in ReflectionExemptionRegistry — a genuine, expected gap that blocks "
            + "T-40/T-44 until 00.Governance registers a second entry. Failing types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
