using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that asserts
/// <c>SharedKernel.Application.Behaviors.IUnitOfWork</c> and
/// <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c> remain two genuinely independent
/// interface declarations — never merged into a single type and never one inheriting the other.
/// </summary>
/// <remarks>
/// <para>
/// This is a <strong>negative-space / regression-guard</strong> rule. Both interfaces are
/// independently declared today (the desired state), so the fire-path test fixtures must be
/// contrived assemblies proving the predicate would catch a future merge attempt. The rule
/// mirrors the technique already established for
/// <c>RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther</c> and
/// <c>CommunicationLayeringRules.GrpcNeverReferencesContracts</c>.
/// </para>
/// <para>
/// <strong>Rationale:</strong> the local-seam pattern (05.Application declares its own
/// <c>IUnitOfWork</c>, bridged to 06.Persistence's <c>IUnitOfWork</c> at the composition root —
/// the same pattern proven for <c>IAuthorizationContext</c> and <c>IIdempotencyKeyStore</c>)
/// only holds if the two interfaces stay genuinely independent. A future "simplification"
/// that merges them or makes one inherit the other would silently reintroduce the
/// <c>05.Application</c> → <c>06.Persistence</c> layering violation the local-seam pattern
/// exists to prevent.
/// </para>
/// <para>
/// <strong>Three independent checks, each a distinct failure mode:</strong>
/// <list type="number">
///   <item><description>
///     <strong>Existence:</strong> both types must be resolvable by exact full name. A missing
///     type (renamed or removed) is itself a seam-pattern violation requiring governance review —
///     it is not silently treated as "passing".
///   </description></item>
///   <item><description>
///     <strong>Identity collapse:</strong> the two resolved <see cref="TypeDefinition"/>s must
///     not be reference-equal after resolution — catches an accidental type-forwarding/alias
///     merge collapsing both names onto one type.
///   </description></item>
///   <item><description>
///     <strong>Bidirectional base-interface check:</strong> neither
///     <see cref="TypeDefinition.Interfaces"/> collection may contain an entry whose
///     <c>InterfaceType.FullName</c> equals the other's full name — catches
///     <c>interface IUnitOfWork : {other}.IUnitOfWork</c> being introduced on either side.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Implementation note:</strong> because the check spans two assemblies but
/// <c>ICustomRule.MeetsRule</c> receives one <see cref="TypeDefinition"/> at a time, the
/// predicate performs the full three-check validation once (lazily on first call) using the
/// two <see cref="ModuleDefinition"/>s captured at construction time, caches the
/// pass/fail decision, and returns the cached value for every subsequent <c>MeetsRule</c>
/// call. This makes the rule effectively an assembly-level invariant assertion wrapped in
/// the per-type NetArchTest API.
/// </para>
/// <para>
/// Reuses the <c>TypeDefinition.Interfaces</c> enumeration pattern already used by
/// <see cref="DoesNotImplementOpenGenericInterfacePredicate"/> and
/// <see cref="SagaStateMustExtendSagaStateBasePredicate"/> — no new technique, no new NuGet
/// dependency.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>
/// // Inside SharedKernel.Application.Behaviors:
/// interface IUnitOfWork : SharedKernel.Persistence.Abstractions.IUnitOfWork { }
/// </code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>
/// // Two independently-declared IUnitOfWork interfaces, bridged only by a concrete adapter
/// // (e.g. EfUnitOfWork implementing both) at the composition root — never by interface
/// // inheritance between the two abstractions themselves.
/// </code>
/// </para>
/// </remarks>
public sealed class UnitOfWorkInterfacesRemainDistinctPredicate : ICustomRule
{
    private const string AppBehaviorsFullName = "SharedKernel.Application.Behaviors.IUnitOfWork";
    private const string PersistenceAbstractionsFullName = "SharedKernel.Persistence.Abstractions.IUnitOfWork";

    private readonly ModuleDefinition _applicationModule;
    private readonly ModuleDefinition _persistenceModule;

    // Lazily evaluated; null means "not yet evaluated".
    private bool? _cachedResult;
    private string? _cachedFailureMessage;

    /// <summary>
    /// Initialises the predicate with the two modules that should contain the respective
    /// <c>IUnitOfWork</c> declarations.
    /// </summary>
    /// <param name="applicationModule">
    /// The <see cref="ModuleDefinition"/> of the <c>SharedKernel.Application.Behaviors</c>
    /// assembly, expected to contain <c>SharedKernel.Application.Behaviors.IUnitOfWork</c>.
    /// </param>
    /// <param name="persistenceModule">
    /// The <see cref="ModuleDefinition"/> of the <c>SharedKernel.Persistence.Abstractions</c>
    /// assembly, expected to contain <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c>.
    /// </param>
    public UnitOfWorkInterfacesRemainDistinctPredicate(
        ModuleDefinition applicationModule,
        ModuleDefinition persistenceModule)
    {
        _applicationModule = applicationModule ?? throw new ArgumentNullException(nameof(applicationModule));
        _persistenceModule = persistenceModule ?? throw new ArgumentNullException(nameof(persistenceModule));
    }

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when the three-check structural invariant
    /// passes; <see langword="false"/> when any check fails. The invariant is evaluated once
    /// on the first call and the result is cached for all subsequent calls.
    /// </summary>
    /// <param name="type">
    /// The Mono.Cecil <see cref="TypeDefinition"/> being evaluated. This parameter is not
    /// inspected directly — the rule evaluates the two <c>IUnitOfWork</c> definitions in
    /// the modules supplied at construction time.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when any of the three checks (existence, identity collapse,
    /// bidirectional base-interface) fails; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        if (_cachedResult.HasValue)
            return _cachedResult.Value;

        _cachedResult = EvaluateInvariant();
        return _cachedResult.Value;
    }

    /// <summary>
    /// Gets the failure message explaining which check failed. Returns <see langword="null"/>
    /// when the predicate has not yet been evaluated or when the invariant passed.
    /// </summary>
    internal string? FailureMessage => _cachedFailureMessage;

    private bool EvaluateInvariant()
    {
        // Check 1 — Existence: both types must be resolvable by exact full name.
        var appType = FindTypeByFullName(_applicationModule, AppBehaviorsFullName);
        if (appType is null)
        {
            _cachedFailureMessage =
                $"Existence check failed: '{AppBehaviorsFullName}' was not found in the " +
                $"application-behaviors assembly '{_applicationModule.Assembly.Name.Name}'. " +
                "A renamed or removed interface is itself a seam-pattern violation requiring " +
                "governance review.";
            return false;
        }

        var persistenceType = FindTypeByFullName(_persistenceModule, PersistenceAbstractionsFullName);
        if (persistenceType is null)
        {
            _cachedFailureMessage =
                $"Existence check failed: '{PersistenceAbstractionsFullName}' was not found in " +
                $"the persistence-abstractions assembly '{_persistenceModule.Assembly.Name.Name}'. " +
                "A renamed or removed interface is itself a seam-pattern violation requiring " +
                "governance review.";
            return false;
        }

        // Check 2 — Identity collapse: the two TypeDefinitions must not be reference-equal.
        if (ReferenceEquals(appType, persistenceType))
        {
            _cachedFailureMessage =
                $"Identity-collapse check failed: '{AppBehaviorsFullName}' and " +
                $"'{PersistenceAbstractionsFullName}' resolved to the same TypeDefinition instance. " +
                "A type-forwarding or alias merge has collapsed both interface names onto one type.";
            return false;
        }

        // Check 3a — App interface must not extend persistence interface.
        if (TypeImplementsInterface(appType, PersistenceAbstractionsFullName))
        {
            _cachedFailureMessage =
                $"Base-interface check failed: '{AppBehaviorsFullName}' extends " +
                $"'{PersistenceAbstractionsFullName}' in its base-interface list. " +
                "Interface inheritance between the two IUnitOfWork declarations silently " +
                "reintroduces the 05.Application → 06.Persistence layering violation.";
            return false;
        }

        // Check 3b — Persistence interface must not extend application interface.
        if (TypeImplementsInterface(persistenceType, AppBehaviorsFullName))
        {
            _cachedFailureMessage =
                $"Base-interface check failed: '{PersistenceAbstractionsFullName}' extends " +
                $"'{AppBehaviorsFullName}' in its base-interface list. " +
                "Interface inheritance between the two IUnitOfWork declarations silently " +
                "reintroduces the 05.Application → 06.Persistence layering violation.";
            return false;
        }

        return true;
    }

    private static TypeDefinition? FindTypeByFullName(ModuleDefinition module, string fullName)
    {
        // TypeDefinition.FullName in Mono.Cecil uses '+' for nested types and '.' for namespaces.
        // The IUnitOfWork types are top-level, so a simple linear scan against FullName works.
        foreach (var type in module.Types)
        {
            if (type.FullName == fullName)
                return type;
        }

        return null;
    }

    private static bool TypeImplementsInterface(TypeDefinition type, string interfaceFullName)
    {
        foreach (var iface in type.Interfaces)
        {
            // Use FullName on the element type (unwrap generic instances if needed).
            var ifaceType = iface.InterfaceType is GenericInstanceType git
                ? git.ElementType
                : iface.InterfaceType;

            if (ifaceType.FullName == interfaceFullName)
                return true;
        }

        return false;
    }
}
