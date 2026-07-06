using FluentAssertions;
using Mono.Cecil;
using SharedKernel.Application.DomainEvents;
using SharedKernel.ArchitectureTests.Predicates;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Real-assembly tests for the SK0012 <c>MakeGenericMethod</c> prohibition against the compiled
/// <c>SharedKernel.Application</c> assembly — introduced by WO-039 P-240
/// (<c>SK.00.DomainEventDispatcherReflectionExemption</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher.PublishSingle</c>
/// contains a documented, deliberate <c>MethodInfo.MakeGenericMethod</c> call (see that type's
/// own XML doc remarks and <c>05.Application/CLAUDE.md</c>), modeled on the already-accepted
/// <c>07.Messaging.MassTransitEventPublisher.BuildPublisher</c> precedent.
/// </para>
/// <para>
/// <strong>Empirical verification note (read before touching the constants below):</strong> the
/// exact <c>(typeFullName, methodName)</c> pair registered in
/// <see cref="ReflectionExemptionRegistry"/> for this exemption is
/// <c>("SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher/&lt;&gt;c",
/// "&lt;PublishSingle&gt;b__8_0")</c> — NOT the source-level "MediatRDomainEventDispatcher"/
/// "PublishSingle" pair a naive reading would suggest. <c>PublishSingle</c>'s
/// <c>MakeGenericMethod</c> call is inside a closure-free <c>static</c> lambda passed to
/// <c>ConcurrentDictionary&lt;Type, Delegate&gt;.GetOrAdd</c>; Roslyn compiles such lambdas onto a
/// compiler-generated <c>&lt;&gt;c</c> singleton nested type with a synthesized
/// <c>&lt;PublishSingle&gt;b__{token}_{ordinal}</c> method name. This pair was determined
/// empirically (red-then-green) by running a temporary Mono.Cecil IL-walk — instrumented
/// identically to <see cref="NoMakeGenericMethodReflectionPredicate"/> — against the real
/// compiled <c>SharedKernel.Application.dll</c>, then copying the verified values into the
/// registry. Re-verify empirically (do not hand-edit from a source-level reading) if
/// <c>PublishSingle</c>'s lambda body or its position within the type ever changes.
/// </para>
/// <para>
/// <strong>Discovered NetArchTest coverage gap (documented here, not silently worked around):</strong>
/// while writing this test it was discovered that <c>NetArchTest.Rules</c>'s own type-discovery
/// layer (<c>Types.InAssembly(assembly)</c>, used internally by
/// <see cref="ReflectionGuardRules.NoMakeGenericMethodReflection"/>) never surfaces
/// compiler-generated closure types (e.g. the <c>&lt;&gt;c</c> singleton display class above) to
/// <em>any</em> <c>ICustomRule</c> — confirmed by instrumenting a recording <c>ICustomRule</c> and
/// observing that <c>&lt;&gt;c</c> is absent from the set of <see cref="TypeDefinition"/> instances
/// NetArchTest visits, with and without the <c>.AreNotAbstract()</c> filter. This means
/// <see cref="ReflectionGuardRules.NoMakeGenericMethodReflection"/>, called end-to-end against the
/// real <c>SharedKernel.Application</c> assembly, currently reports success REGARDLESS of whether
/// <c>MediatRDomainEventDispatcher</c>'s exemption is registered — the violation is never reached.
/// This is a limitation of NetArchTest's own type enumeration, not of
/// <see cref="NoMakeGenericMethodReflectionPredicate"/>'s IL-walk logic, which correctly evaluates
/// the exemption once given the type — see the predicate-level tests below, which bypass
/// NetArchTest's type discovery and invoke <see cref="NoMakeGenericMethodReflectionPredicate.MeetsRule"/>
/// directly against the real, Mono.Cecil-loaded <c>&lt;&gt;c</c> <see cref="TypeDefinition"/> to
/// prove the exemption is genuinely load-bearing at the enforcement layer that actually runs. This
/// phase's scope (P-240) forbids changing <see cref="NoMakeGenericMethodReflectionPredicate"/> or
/// <see cref="ReflectionGuardRules"/> logic; closing the NetArchTest type-discovery gap itself is
/// therefore explicitly out of scope here and is recorded in <c>00.Governance/CLAUDE.md</c> as a
/// candidate follow-up work order.
/// </para>
/// </remarks>
public class ReflectionGuardRulesRealAssemblyTests
{
    private const string ExemptedTypeFullName =
        "SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher/<>c";

    private const string ExemptedMethodName = "<PublishSingle>b__8_0";

    // ---------------------------------------------------------------------------
    // T-169 — Fire path (predicate-level): the real violation fails when unregistered
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-169: with <see cref="ReflectionExemptionRegistry"/>'s <c>MediatRDomainEventDispatcher</c>
    /// entry temporarily removed via the existing internal <c>Unregister</c> test hook,
    /// <see cref="NoMakeGenericMethodReflectionPredicate.MeetsRule"/> evaluated directly against
    /// the REAL, Mono.Cecil-loaded <c>&lt;&gt;c</c> closure <see cref="TypeDefinition"/> from the
    /// compiled <c>SharedKernel.Application</c> assembly returns <see langword="false"/> (rule
    /// violated) — proving the shipped exemption is load-bearing, not a vacuous pass that would
    /// succeed even without it. Invoked at the predicate layer (not via
    /// <see cref="ReflectionGuardRules.NoMakeGenericMethodReflection"/>) because NetArchTest's own
    /// type-discovery layer never surfaces this compiler-generated type to any
    /// <c>ICustomRule</c> — see the class-level remarks' "Discovered NetArchTest coverage gap."
    /// </summary>
    [Fact]
    public void MeetsRule_RealClosureType_ExemptionUnregistered_PredicateFails()
    {
        ReflectionExemptionRegistry.IsExempt(ExemptedTypeFullName, ExemptedMethodName)
            .Should().BeTrue(because: "the shipped registry entry must be present before this test removes it");

        var closureType = LoadRealClosureType();

        ReflectionExemptionRegistry.Unregister(ExemptedTypeFullName, ExemptedMethodName);

        try
        {
            var predicate = new NoMakeGenericMethodReflectionPredicate();

            predicate.MeetsRule(closureType).Should().BeFalse(
                because: "MediatRDomainEventDispatcher's PublishSingle closure calls MakeGenericMethod " +
                          "and the exemption was just removed, so the predicate must flag it");
        }
        finally
        {
            ReflectionExemptionRegistry.Register(ExemptedTypeFullName, ExemptedMethodName);
        }
    }

    // ---------------------------------------------------------------------------
    // T-170 — Pass path (predicate-level): the real violation passes when exempted
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-170: with <see cref="ReflectionExemptionRegistry"/>'s shipped
    /// <c>MediatRDomainEventDispatcher</c> entry in place (the normal, permanent state),
    /// <see cref="NoMakeGenericMethodReflectionPredicate.MeetsRule"/> evaluated directly against
    /// the real <c>&lt;&gt;c</c> closure <see cref="TypeDefinition"/> returns
    /// <see langword="true"/> (rule met).
    /// </summary>
    [Fact]
    public void MeetsRule_RealClosureType_ExemptionRegistered_PredicatePasses()
    {
        ReflectionExemptionRegistry.IsExempt(ExemptedTypeFullName, ExemptedMethodName)
            .Should().BeTrue(because: "the MediatRDomainEventDispatcher exemption ships registered by default");

        var closureType = LoadRealClosureType();
        var predicate = new NoMakeGenericMethodReflectionPredicate();

        predicate.MeetsRule(closureType).Should().BeTrue(
            because: "MediatRDomainEventDispatcher's PublishSingle MakeGenericMethod call is " +
                      "registered in ReflectionExemptionRegistry (WO-039 P-240)");
    }

    // ---------------------------------------------------------------------------
    // Documentation test — records the current, discovered NetArchTest end-to-end behavior
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Documents (does not merely assert incidentally) that
    /// <see cref="ReflectionGuardRules.NoMakeGenericMethodReflection"/>, invoked end-to-end
    /// against the real <c>SharedKernel.Application</c> assembly, currently reports success
    /// regardless of <see cref="ReflectionExemptionRegistry"/> state for the
    /// <c>MediatRDomainEventDispatcher</c> case, because NetArchTest's own type discovery never
    /// visits the compiler-generated <c>&lt;&gt;c</c> closure type that actually contains the
    /// <c>MakeGenericMethod</c> call. See the class-level remarks for the full explanation. This
    /// test exists so a future NetArchTest upgrade or type-discovery fix that starts surfacing
    /// compiler-generated types will be caught by a changed assertion here, rather than silently
    /// changing behavior unnoticed.
    /// </summary>
    [Fact]
    public void NoMakeGenericMethodReflection_RealApplicationAssembly_CurrentlySucceedsRegardlessOfRegistryState()
    {
        var assembly = typeof(MediatRDomainEventDispatcher).Assembly;

        var withExemption = ReflectionGuardRules.NoMakeGenericMethodReflection(assembly).GetResult();
        withExemption.IsSuccessful.Should().BeTrue(
            because: "the exemption ships registered, and the rule should pass in the normal, permanent state");

        ReflectionExemptionRegistry.Unregister(ExemptedTypeFullName, ExemptedMethodName);
        try
        {
            var withoutExemption = ReflectionGuardRules.NoMakeGenericMethodReflection(assembly).GetResult();

            withoutExemption.IsSuccessful.Should().BeTrue(
                because: "NetArchTest's type discovery never visits the <>c compiler-generated closure " +
                          "type containing PublishSingle's MakeGenericMethod call, so the end-to-end rule " +
                          "cannot currently observe this specific violation even with no exemption " +
                          "registered — a documented NetArchTest limitation, not a predicate defect (see " +
                          "the predicate-level tests above, which prove the predicate itself is correct)");
        }
        finally
        {
            ReflectionExemptionRegistry.Register(ExemptedTypeFullName, ExemptedMethodName);
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Loads the real, compiled <c>SharedKernel.Application.dll</c> via Mono.Cecil and returns the
    /// <see cref="TypeDefinition"/> for <c>MediatRDomainEventDispatcher</c>'s compiler-generated
    /// <c>&lt;&gt;c</c> closure display class — the actual type that contains
    /// <c>PublishSingle</c>'s <c>MakeGenericMethod</c> call, which NetArchTest's own type
    /// discovery never surfaces (see class-level remarks).
    /// </summary>
    private static TypeDefinition LoadRealClosureType()
    {
        var assembly = typeof(MediatRDomainEventDispatcher).Assembly;
        var assemblyDefinition = AssemblyDefinition.ReadAssembly(assembly.Location);

        var dispatcherType = assemblyDefinition.MainModule.Types
            .Single(t => t.FullName == "SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher");

        var closureType = dispatcherType.NestedTypes.Single(t => t.Name == "<>c");

        closureType.FullName.Should().Be(ExemptedTypeFullName,
            because: "the empirically-verified type full name must still match the compiled assembly");

        closureType.Methods.Should().Contain(m => m.Name == ExemptedMethodName,
            because: "the empirically-verified method name must still exist on the compiled closure type");

        return closureType;
    }
}
