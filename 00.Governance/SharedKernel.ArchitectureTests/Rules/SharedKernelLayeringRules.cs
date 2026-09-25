using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// The dependency purity rules that the package tiers cannot express.
/// </summary>
/// <remarks>
/// <para>
/// Which package may reference which is enforced by the build: every kernel csproj declares a
/// <c>&lt;SharedKernelTier&gt;</c> and <c>eng/SharedKernelTiers.targets</c> fails the build (SKTIER000–005)
/// on an edge the tier matrix does not allow (WO-086). The numbered-layer rules that used to live here
/// ("domain NN may reference only domains below NN") were deleted in P-574 because the tier check
/// covers them.
/// </para>
/// <para>
/// What remains are rules between packages of the <em>same</em> tier, or rules a consuming service
/// applies to its own assemblies:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ContractsNeverReferencesDomain"/> — both are Model tier.</description></item>
/// <item><description><see cref="DomainNeverReferencesContracts"/> — both are Model tier.</description></item>
/// <item><description><see cref="ModelNeverReferencesLogging"/> — the logging abstractions pass the tier allow-list.</description></item>
/// <item><description><see cref="TestingNeverReferencedByProduction"/> — also usable against a consuming service's assemblies.</description></item>
/// </list>
/// </remarks>
public static class SharedKernelLayeringRules
{
    private const string DomainNamespace = "SharedKernel.Domain";
    private const string ContractsNamespace = "SharedKernel.Contracts";
    private const string TestingNamespace = "SharedKernel.Testing";
    private const string LoggingNamespace = "Microsoft.Extensions.Logging";

    /// <summary>The packable consumer-facing persistence test helpers (16.Testing, P-558): test projects only.</summary>
    public const string PersistenceTestingNamespace = "SharedKernel.Persistence.Testing";

    /// <summary>
    /// <c>SharedKernel.Contracts</c> must never reference <c>SharedKernel.Domain</c>, although both are Model tier.
    /// </summary>
    /// <remarks>
    /// A wire contract is an independent, versioned projection of a domain model, never the model itself: a
    /// contract that references domain types changes shape whenever the model does, and drags the domain package
    /// into every consumer. <c>SharedKernel.Primitives</c> (<c>Error</c>, <c>ValidationResult&lt;T&gt;</c>) stays
    /// permitted.
    /// </remarks>
    /// <param name="assembly">The Contracts assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting no Contracts type depends on <c>SharedKernel.Domain</c>.</returns>
    public static ConditionList ContractsNeverReferencesDomain(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(DomainNamespace);

    /// <summary>
    /// <c>SharedKernel.Domain</c> must never reference <c>SharedKernel.Contracts</c>, although both are Model tier.
    /// </summary>
    /// <remarks>
    /// The domain model does not know how it is serialized across a service boundary; mapping a domain event to an
    /// integration event happens at the edge of the publishing service.
    /// </remarks>
    /// <param name="assembly">The Domain assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting no Domain type depends on <c>SharedKernel.Contracts</c>.</returns>
    public static ConditionList DomainNeverReferencesContracts(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(ContractsNamespace);

    /// <summary>
    /// A Model-tier assembly (<c>SharedKernel.Domain</c>, <c>SharedKernel.Contracts</c>, or a service's own domain or
    /// contracts project) must never reference <c>Microsoft.Extensions.Logging</c>.
    /// </summary>
    /// <remarks>
    /// <c>ILogger</c> is an infrastructure concern; domain and contract types stay logging-free. The tier check
    /// cannot express this: <c>Microsoft.Extensions.Logging.Abstractions</c> matches the Model/Abstractions allow-list
    /// (<c>Microsoft.Extensions.*.Abstractions</c>).
    /// </remarks>
    /// <param name="assembly">The Domain or Contracts assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting no type depends on <c>Microsoft.Extensions.Logging</c>.</returns>
    public static ConditionList ModelNeverReferencesLogging(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(LoggingNamespace);

    /// <summary>
    /// Testing packages must never be referenced by production code.
    /// Testing helpers are dev/test-time only and must never appear as transitive dependencies.
    /// Every testing package (the core <c>SharedKernel.Testing</c>, each <c>SharedKernel.{Capability}.Testing</c> and
    /// <c>SharedKernel.Testing.Internal</c>) declares its types under the <c>SharedKernel.Testing</c> namespace, except the
    /// persistence doubles under <c>SharedKernel.Persistence.Testing</c> (P-571).
    /// </summary>
    /// <param name="assembly">The production assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting no production type depends on Testing packages.</returns>
    public static ConditionList TestingNeverReferencedByProduction(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(TestingNamespace)
            .And()
            .NotHaveDependencyOn(PersistenceTestingNamespace);
}
