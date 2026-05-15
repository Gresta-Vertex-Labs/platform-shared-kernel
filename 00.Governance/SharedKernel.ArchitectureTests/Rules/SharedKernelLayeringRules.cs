using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest layering rules for the SharedKernel architecture.
/// Each factory method corresponds 1:1 to a constraint in the root CLAUDE.md layering table.
/// </summary>
/// <remarks>
/// Use these in your architecture test project with a given assembly:
/// <code>
/// var result = SharedKernelLayeringRules.DomainNeverReferencesPersistence(domainAssembly).GetResult();
/// result.IsSuccessful.Should().BeTrue();
/// </code>
/// </remarks>
public static class SharedKernelLayeringRules
{
    // Namespace constants matching the SharedKernel package convention
    private const string CachingNamespace = "SharedKernel.Caching";
    private const string DomainNamespace = "SharedKernel.Domain";
    private const string ContractsNamespace = "SharedKernel.Contracts";
    private const string PersistenceNamespace = "SharedKernel.Persistence";
    private const string MessagingNamespace = "SharedKernel.Messaging";
    private const string StorageNamespace = "SharedKernel.Storage";
    private const string SearchNamespace = "SharedKernel.Search";
    private const string TestingNamespace = "SharedKernel.Testing";

    /// <summary>
    /// 01.Core — references nothing. Core types must not depend on any other SharedKernel domain.
    /// </summary>
    /// <param name="assembly">The Core assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Core types have no SharedKernel dependencies.</returns>
    public static ConditionList CoreReferencesNothing(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(CachingNamespace)
            .And()
            .NotHaveDependencyOn(DomainNamespace)
            .And()
            .NotHaveDependencyOn(ContractsNamespace)
            .And()
            .NotHaveDependencyOn(PersistenceNamespace)
            .And()
            .NotHaveDependencyOn(MessagingNamespace);

    /// <summary>
    /// 02.Caching — may only reference 01.Core. Must not reference Domain, Contracts, or any
    /// infrastructure layer.
    /// </summary>
    /// <param name="assembly">The Caching assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Caching types do not reference layers above Core.</returns>
    public static ConditionList CachingReferencesOnlyCore(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(DomainNamespace)
            .And()
            .NotHaveDependencyOn(ContractsNamespace)
            .And()
            .NotHaveDependencyOn(PersistenceNamespace)
            .And()
            .NotHaveDependencyOn(MessagingNamespace);

    /// <summary>
    /// 03.Domain — may only reference 01.Core. Must not reference Caching, Contracts, or infrastructure.
    /// </summary>
    /// <param name="assembly">The Domain assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Domain types only reference Core.</returns>
    public static ConditionList DomainReferencesOnlyCore(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(CachingNamespace)
            .And()
            .NotHaveDependencyOn(ContractsNamespace)
            .And()
            .NotHaveDependencyOn(PersistenceNamespace)
            .And()
            .NotHaveDependencyOn(MessagingNamespace);

    /// <summary>
    /// 04.Contracts — may reference 01.Core and 03.Domain only. Must not reference infrastructure.
    /// </summary>
    /// <param name="assembly">The Contracts assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Contracts types do not reference infrastructure layers.</returns>
    public static ConditionList ContractsReferencesOnlyCoreAndDomain(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(CachingNamespace)
            .And()
            .NotHaveDependencyOn(PersistenceNamespace)
            .And()
            .NotHaveDependencyOn(MessagingNamespace)
            .And()
            .NotHaveDependencyOn(StorageNamespace)
            .And()
            .NotHaveDependencyOn(SearchNamespace);

    /// <summary>
    /// Hard rule: 03.Domain must never reference 06.Persistence or any persistence infrastructure.
    /// Violating this rule would couple domain logic to storage concerns.
    /// </summary>
    /// <param name="assembly">The Domain assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Domain types have no dependency on Persistence.</returns>
    public static ConditionList DomainNeverReferencesPersistence(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(PersistenceNamespace);

    /// <summary>
    /// Hard rule: 03.Domain must never reference 07.Messaging or any messaging infrastructure.
    /// Domain events are raised by the domain — never dispatched directly from it.
    /// </summary>
    /// <param name="assembly">The Domain assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Domain types have no dependency on Messaging.</returns>
    public static ConditionList DomainNeverReferencesMessaging(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(MessagingNamespace);

    /// <summary>
    /// Hard rule: 05.Application must never reference concrete infrastructure packages.
    /// Application logic depends on abstractions only (interfaces, not implementations).
    /// </summary>
    /// <param name="assembly">The Application assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Application types do not reference concrete infrastructure.</returns>
    public static ConditionList ApplicationNeverReferencesConcreteInfrastructure(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.Persistence.EfCore")
            .And()
            .NotHaveDependencyOn("SharedKernel.Persistence.PostgreSQL")
            .And()
            .NotHaveDependencyOn("SharedKernel.Persistence.Dapper")
            .And()
            .NotHaveDependencyOn("SharedKernel.Messaging.MassTransit")
            .And()
            .NotHaveDependencyOn("SharedKernel.Caching.Redis")
            .And()
            .NotHaveDependencyOn("SharedKernel.Storage.S3")
            .And()
            .NotHaveDependencyOn("SharedKernel.Search.Meilisearch")
            .And()
            .NotHaveDependencyOn("SharedKernel.Search.ElasticSearch");

    /// <summary>
    /// Hard rule: 16.Testing packages must never be referenced by production code.
    /// Testing helpers are dev/test-time only and must never appear as transitive dependencies.
    /// </summary>
    /// <param name="assembly">The production assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting no production type depends on Testing packages.</returns>
    public static ConditionList TestingNeverReferencedByProduction(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(TestingNamespace);
}
