using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="SharedKernelLayeringRules"/> — specifically
/// <see cref="SharedKernelLayeringRules.DomainNeverReferencesPersistence"/>.
/// Assemblies are compiled in-memory via Roslyn so no external fixture DLLs are needed.
/// </summary>
public class LayeringRulesTests
{
    // ---------------------------------------------------------------------------
    // ContractsReferencesOnlyCore — 04.Contracts reaches nothing but 01.Core
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Fire path: a contracts-shaped assembly that depends on <c>SharedKernel.Domain</c> fails
    /// <see cref="SharedKernelLayeringRules.ContractsReferencesOnlyCore"/> — 03.Domain is no longer a
    /// permitted dependency of 04.Contracts.
    /// </summary>
    [Fact]
    public void ContractsReferencesOnlyCore_DomainDependency_RuleFails()
    {
        const string violationSource = """
            namespace SharedKernel.Domain
            {
                public interface IDomainEvent { }
            }

            namespace SharedKernel.Contracts
            {
                public sealed class OrderPlacedDto
                {
                    public SharedKernel.Domain.IDomainEvent? Source { get; set; }
                }
            }
            """;

        var violationAssembly = CompileInMemory("ContractsDomainViolation", violationSource);

        var result = SharedKernelLayeringRules.ContractsReferencesOnlyCore(violationAssembly).GetResult();

        result.IsSuccessful.Should().BeFalse(because: "OrderPlacedDto references SharedKernel.Domain");
    }

    /// <summary>
    /// Fire path: a contracts-shaped assembly that depends on an infrastructure layer still fails.
    /// </summary>
    [Fact]
    public void ContractsReferencesOnlyCore_PersistenceDependency_RuleFails()
    {
        const string violationSource = """
            namespace SharedKernel.Persistence
            {
                public interface IRepository { }
            }

            namespace SharedKernel.Contracts
            {
                public sealed class LeakyDto
                {
                    public SharedKernel.Persistence.IRepository? Repository { get; set; }
                }
            }
            """;

        var violationAssembly = CompileInMemory("ContractsPersistenceViolation", violationSource);

        var result = SharedKernelLayeringRules.ContractsReferencesOnlyCore(violationAssembly).GetResult();

        result.IsSuccessful.Should().BeFalse(because: "LeakyDto references SharedKernel.Persistence");
    }

    /// <summary>
    /// Pass path: a contracts-shaped assembly that depends only on <c>SharedKernel.Primitives</c> passes.
    /// </summary>
    [Fact]
    public void ContractsReferencesOnlyCore_PrimitivesDependencyOnly_RulePasses()
    {
        const string cleanSource = """
            namespace SharedKernel.Primitives
            {
                public sealed class Error { }
            }

            namespace SharedKernel.Contracts
            {
                public sealed class PageRequest
                {
                    public static SharedKernel.Primitives.Error? Validate(int page) => page < 1 ? new SharedKernel.Primitives.Error() : null;
                }
            }
            """;

        var cleanAssembly = CompileInMemory("ContractsClean", cleanSource);

        var result = SharedKernelLayeringRules.ContractsReferencesOnlyCore(cleanAssembly).GetResult();

        result.IsSuccessful.Should().BeTrue(because: "PageRequest depends only on SharedKernel.Primitives");
    }

    /// <summary>
    /// Pass path: the real <c>SharedKernel.Contracts</c> assembly references nothing but 01.Core.
    /// </summary>
    [Fact]
    public void ContractsReferencesOnlyCore_RealContractsAssembly_RulePasses()
    {
        var contractsAssembly = typeof(SharedKernel.Contracts.Events.EventEnvelope).Assembly;

        var result = SharedKernelLayeringRules.ContractsReferencesOnlyCore(contractsAssembly).GetResult();

        result.IsSuccessful.Should().BeTrue(because: "SharedKernel.Contracts references only SharedKernel.Primitives");
    }

    /// <summary>
    /// T-12 (fire path): an assembly whose domain type depends on SharedKernel.Persistence
    /// must cause the rule to fail.
    /// </summary>
    [Fact]
    public void DomainNeverReferencesPersistence_ViolationAssembly_RuleFails()
    {
        // Arrange — compile an assembly where a domain type references SharedKernel.Persistence
        const string violationSource = """
            namespace SharedKernel.Persistence
            {
                public interface IRepository { }
            }

            namespace SharedKernel.Domain
            {
                public class OrderAggregate
                {
                    private readonly SharedKernel.Persistence.IRepository _repo;
                    public OrderAggregate(SharedKernel.Persistence.IRepository repo) { _repo = repo; }
                }
            }
            """;

        var violationAssembly = CompileInMemory("ViolationDomain", violationSource);

        // Act
        var conditionList = SharedKernelLayeringRules.DomainNeverReferencesPersistence(violationAssembly);
        var result = conditionList.GetResult();

        // Assert — rule must detect the forbidden dependency
        result.IsSuccessful.Should().BeFalse(
            because: "OrderAggregate references SharedKernel.Persistence.IRepository");
    }

    /// <summary>
    /// T-12 (pass path): a clean assembly with no persistence dependency must pass the rule.
    /// </summary>
    [Fact]
    public void DomainNeverReferencesPersistence_CleanAssembly_RulePasses()
    {
        // Arrange — compile an assembly with a domain type that has no Persistence reference
        const string cleanSource = """
            namespace SharedKernel.Domain
            {
                public class OrderAggregate
                {
                    public string Id { get; } = string.Empty;
                    public string Description { get; set; } = string.Empty;
                }
            }
            """;

        var cleanAssembly = CompileInMemory("CleanDomain", cleanSource);

        // Act
        var conditionList = SharedKernelLayeringRules.DomainNeverReferencesPersistence(cleanAssembly);
        var result = conditionList.GetResult();

        // Assert — no forbidden dependency present
        result.IsSuccessful.Should().BeTrue(
            because: "OrderAggregate has no reference to SharedKernel.Persistence");
    }

    // ---------------------------------------------------------------------------
    // ApplicationBehaviorsNeverReferencesCachingPollyHostingOrCore — P-544
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Fire path: a Behaviors-shaped assembly that depends on <c>SharedKernel.Caching.Abstractions</c>
    /// fails <see cref="SharedKernelLayeringRules.ApplicationBehaviorsNeverReferencesCachingPollyHostingOrCore"/> —
    /// that reference belongs exclusively to the sibling <c>SharedKernel.Application.Pipeline.Caching</c>
    /// package as of P-544.
    /// </summary>
    [Fact]
    public void ApplicationBehaviorsNeverReferencesCachingPollyHostingOrCore_CachingAbstractionsDependency_RuleFails()
    {
        const string violationSource = """
            namespace SharedKernel.Caching.Abstractions
            {
                public interface ICacheService { }
            }

            namespace SharedKernel.Application.Pipeline
            {
                public sealed class LeakyBehavior
                {
                    public SharedKernel.Caching.Abstractions.ICacheService? Cache { get; set; }
                }
            }
            """;

        var violationAssembly = CompileInMemory("BehaviorsCachingViolation", violationSource);

        var result = SharedKernelLayeringRules
            .ApplicationBehaviorsNeverReferencesCachingPollyHostingOrCore(violationAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LeakyBehavior references SharedKernel.Caching.Abstractions, which now belongs " +
                     "exclusively to SharedKernel.Application.Pipeline.Caching");
    }

    /// <summary>
    /// Pass path: a Behaviors-shaped assembly with no dependency on Caching, Polly, Hosting, or
    /// Core passes <see cref="SharedKernelLayeringRules.ApplicationBehaviorsNeverReferencesCachingPollyHostingOrCore"/>.
    /// </summary>
    [Fact]
    public void ApplicationBehaviorsNeverReferencesCachingPollyHostingOrCore_CleanAssembly_RulePasses()
    {
        const string cleanSource = """
            namespace SharedKernel.Application.Pipeline
            {
                public sealed class CleanBehavior
                {
                    public string Name { get; } = string.Empty;
                }
            }
            """;

        var cleanAssembly = CompileInMemory("BehaviorsCachingClean", cleanSource);

        var result = SharedKernelLayeringRules
            .ApplicationBehaviorsNeverReferencesCachingPollyHostingOrCore(cleanAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "CleanBehavior has no dependency on Caching, Polly, Hosting, or Core");
    }

    // ---------------------------------------------------------------------------
    // ApplicationBehaviorsCachingNeverReferencesConcreteInfrastructure — P-544
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Fire path: a Behaviors.Caching-shaped assembly that depends on
    /// <c>SharedKernel.Caching.Redis</c> fails
    /// <see cref="SharedKernelLayeringRules.ApplicationBehaviorsCachingNeverReferencesConcreteInfrastructure"/>.
    /// </summary>
    [Fact]
    public void ApplicationBehaviorsCachingNeverReferencesConcreteInfrastructure_RedisDependency_RuleFails()
    {
        const string violationSource = """
            namespace SharedKernel.Caching.Redis
            {
                public interface IConnectionMultiplexerAdapter { }
            }

            namespace SharedKernel.Application.Pipeline.Caching
            {
                public sealed class LeakyCachingBehavior
                {
                    public SharedKernel.Caching.Redis.IConnectionMultiplexerAdapter? Redis { get; set; }
                }
            }
            """;

        var violationAssembly = CompileInMemory("BehaviorsCachingRedisViolation", violationSource);

        var result = SharedKernelLayeringRules
            .ApplicationBehaviorsCachingNeverReferencesConcreteInfrastructure(violationAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LeakyCachingBehavior references the concrete SharedKernel.Caching.Redis package");
    }

    /// <summary>
    /// Pass path: a Behaviors.Caching-shaped assembly depending only on
    /// <c>SharedKernel.Caching.Abstractions</c> passes
    /// <see cref="SharedKernelLayeringRules.ApplicationBehaviorsCachingNeverReferencesConcreteInfrastructure"/>.
    /// </summary>
    [Fact]
    public void ApplicationBehaviorsCachingNeverReferencesConcreteInfrastructure_AbstractionsOnly_RulePasses()
    {
        const string cleanSource = """
            namespace SharedKernel.Caching.Abstractions
            {
                public interface ICacheService { }
            }

            namespace SharedKernel.Application.Pipeline.Caching
            {
                public sealed class CleanCachingBehavior
                {
                    public SharedKernel.Caching.Abstractions.ICacheService? Cache { get; set; }
                }
            }
            """;

        var cleanAssembly = CompileInMemory("BehaviorsCachingAbstractionsOnly", cleanSource);

        var result = SharedKernelLayeringRules
            .ApplicationBehaviorsCachingNeverReferencesConcreteInfrastructure(cleanAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "CleanCachingBehavior depends only on SharedKernel.Caching.Abstractions");
    }

    // ---------------------------------------------------------------------------
    // T-212 — Fire path: contrived 09.Search-shaped fixture references a stubbed forbidden-domain
    // type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-212: When a contrived "SharedKernel.Search"-shaped assembly references a type in a
    /// namespace simulating a forbidden capability domain (here, <c>SharedKernel.Persistence</c>),
    /// <see cref="SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts"/> must fail on
    /// the corresponding array element only.
    /// </summary>
    [Fact]
    public void SearchReferencesOnlyCoreAndContracts_ViolatingAssembly_CorrespondingElementFails()
    {
        const string violationSource = """
            namespace SharedKernel.Persistence
            {
                public interface IRepository { }
            }

            namespace SharedKernel.Search
            {
                public class LeakySearchIndex
                {
                    private readonly SharedKernel.Persistence.IRepository _repository;
                    public LeakySearchIndex(SharedKernel.Persistence.IRepository repository)
                    {
                        _repository = repository;
                    }
                }
            }
            """;

        var violationAssembly = CompileInMemory("ViolationSearch", violationSource);

        var conditionLists = SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts(violationAssembly);

        conditionLists.Should().HaveCount(15);

        var persistenceElementIndex = Array.IndexOf(ForbiddenTermsMirror, "SharedKernel.Persistence");
        persistenceElementIndex.Should().BeGreaterThanOrEqualTo(0);

        for (var i = 0; i < conditionLists.Length; i++)
        {
            var result = conditionLists[i].GetResult();
            if (i == persistenceElementIndex)
            {
                result.IsSuccessful.Should().BeFalse(
                    because: "LeakySearchIndex references SharedKernel.Persistence.IRepository directly");
            }
            else
            {
                result.IsSuccessful.Should().BeTrue(
                    because: "LeakySearchIndex has no dependency on any other forbidden capability domain");
            }
        }
    }

    // ---------------------------------------------------------------------------
    // T-213 — Pass path: contrived 09.Search-shaped fixture references only stubbed
    // SharedKernel.Primitives/SharedKernel.Contracts types
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-213: A contrived "SharedKernel.Search"-shaped assembly referencing only stubbed
    /// <c>SharedKernel.Primitives</c>/<c>SharedKernel.Contracts</c> types must pass every array
    /// element of <see cref="SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts"/>.
    /// </summary>
    [Fact]
    public void SearchReferencesOnlyCoreAndContracts_CleanAssembly_AllElementsPass()
    {
        const string cleanSource = """
            namespace SharedKernel.Primitives
            {
                public class Result { }
            }

            namespace SharedKernel.Contracts
            {
                public class PagedList { }
            }

            namespace SharedKernel.Search
            {
                public class CleanSearchIndex
                {
                    public SharedKernel.Primitives.Result DoWork() => new SharedKernel.Primitives.Result();
                    public SharedKernel.Contracts.PagedList ToPagedList() => new SharedKernel.Contracts.PagedList();
                }
            }
            """;

        var cleanAssembly = CompileInMemory("CleanSearch", cleanSource);

        var conditionLists = SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts(cleanAssembly);

        conditionLists.Should().HaveCount(15);
        foreach (var conditionList in conditionLists)
        {
            conditionList.GetResult().IsSuccessful.Should().BeTrue(
                because: "CleanSearchIndex depends only on SharedKernel.Primitives and SharedKernel.Contracts");
        }
    }

    // ---------------------------------------------------------------------------
    // T-231 — Fire path: contrived 10.Intelligence-shaped fixture references a stubbed
    // forbidden-domain type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-231: When a contrived "SharedKernel.AI"-shaped assembly references a type in a namespace
    /// simulating a forbidden capability domain (here, <c>SharedKernel.Persistence</c>),
    /// <see cref="SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts"/> must fail
    /// on the corresponding array element only.
    /// </summary>
    [Fact]
    public void IntelligenceReferencesOnlyCoreAndContracts_ViolatingAssembly_CorrespondingElementFails()
    {
        const string violationSource = """
            namespace SharedKernel.Persistence
            {
                public interface IRepository { }
            }

            namespace SharedKernel.AI
            {
                public class LeakyVectorCollection
                {
                    private readonly SharedKernel.Persistence.IRepository _repository;
                    public LeakyVectorCollection(SharedKernel.Persistence.IRepository repository)
                    {
                        _repository = repository;
                    }
                }
            }
            """;

        var violationAssembly = CompileInMemory("ViolationIntelligence", violationSource);

        var conditionLists = SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts(violationAssembly);

        conditionLists.Should().HaveCount(15);

        var persistenceElementIndex = Array.IndexOf(IntelligenceForbiddenTermsMirror, "SharedKernel.Persistence");
        persistenceElementIndex.Should().BeGreaterThanOrEqualTo(0);

        for (var i = 0; i < conditionLists.Length; i++)
        {
            var result = conditionLists[i].GetResult();
            if (i == persistenceElementIndex)
            {
                result.IsSuccessful.Should().BeFalse(
                    because: "LeakyVectorCollection references SharedKernel.Persistence.IRepository directly");
            }
            else
            {
                result.IsSuccessful.Should().BeTrue(
                    because: "LeakyVectorCollection has no dependency on any other forbidden capability domain");
            }
        }
    }

    /// <summary>
    /// T-231 (Search variant): a contrived "SharedKernel.AI"-shaped assembly referencing a type
    /// simulating <c>SharedKernel.Search</c> (a sibling domain 10.Intelligence must never
    /// reference) must fail on the corresponding array element only — proves the symmetric swap
    /// versus <see cref="SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts"/>'s own
    /// list (which forbids <c>"SharedKernel.AI"</c> instead).
    /// </summary>
    [Fact]
    public void IntelligenceReferencesOnlyCoreAndContracts_SearchDependency_CorrespondingElementFails()
    {
        const string violationSource = """
            namespace SharedKernel.Search
            {
                public interface ISearchIndex { }
            }

            namespace SharedKernel.AI
            {
                public class LeakyVectorCollection
                {
                    private readonly SharedKernel.Search.ISearchIndex _searchIndex;
                    public LeakyVectorCollection(SharedKernel.Search.ISearchIndex searchIndex)
                    {
                        _searchIndex = searchIndex;
                    }
                }
            }
            """;

        var violationAssembly = CompileInMemory("ViolationIntelligenceSearch", violationSource);

        var conditionLists = SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts(violationAssembly);

        conditionLists.Should().HaveCount(15);

        var searchElementIndex = Array.IndexOf(IntelligenceForbiddenTermsMirror, "SharedKernel.Search");
        searchElementIndex.Should().BeGreaterThanOrEqualTo(0);

        for (var i = 0; i < conditionLists.Length; i++)
        {
            var result = conditionLists[i].GetResult();
            if (i == searchElementIndex)
            {
                result.IsSuccessful.Should().BeFalse(
                    because: "LeakyVectorCollection references SharedKernel.Search.ISearchIndex directly");
            }
            else
            {
                result.IsSuccessful.Should().BeTrue(
                    because: "LeakyVectorCollection has no dependency on any other forbidden capability domain");
            }
        }
    }

    // ---------------------------------------------------------------------------
    // T-232 — Pass path: contrived 10.Intelligence-shaped fixture references only stubbed
    // SharedKernel.Primitives/SharedKernel.Contracts types
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-232: A contrived "SharedKernel.AI"-shaped assembly referencing only stubbed
    /// <c>SharedKernel.Primitives</c>/<c>SharedKernel.Contracts</c> types must pass every array
    /// element of <see cref="SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts"/>.
    /// </summary>
    [Fact]
    public void IntelligenceReferencesOnlyCoreAndContracts_CleanAssembly_AllElementsPass()
    {
        const string cleanSource = """
            namespace SharedKernel.Primitives
            {
                public class Result { }
            }

            namespace SharedKernel.Contracts
            {
                public class PagedList { }
            }

            namespace SharedKernel.AI
            {
                public class CleanVectorCollection
                {
                    public SharedKernel.Primitives.Result DoWork() => new SharedKernel.Primitives.Result();
                    public SharedKernel.Contracts.PagedList ToPagedList() => new SharedKernel.Contracts.PagedList();
                }
            }
            """;

        var cleanAssembly = CompileInMemory("CleanIntelligence", cleanSource);

        var conditionLists = SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts(cleanAssembly);

        conditionLists.Should().HaveCount(15);
        foreach (var conditionList in conditionLists)
        {
            conditionList.GetResult().IsSuccessful.Should().BeTrue(
                because: "CleanVectorCollection depends only on SharedKernel.Primitives and SharedKernel.Contracts");
        }
    }

    // ---------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Mirrors the fifteen-term forbidden list private to
    /// <see cref="SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts"/> — kept here,
    /// not reflected out of the production type, purely so T-212 can locate the array index that
    /// corresponds to <c>"SharedKernel.Persistence"</c> without hard-coding a fragile literal
    /// index.
    /// </summary>
    private static readonly string[] ForbiddenTermsMirror =
    [
        "SharedKernel.Caching",
        "SharedKernel.Domain",
        "SharedKernel.Application",
        "SharedKernel.Persistence",
        "SharedKernel.Messaging",
        "SharedKernel.Storage",
        "SharedKernel.AI",
        "SharedKernel.Communication",
        "SharedKernel.Security",
        "SharedKernel.ServiceDefaults",
        "SharedKernel.MultiTenancy",
        "SharedKernel.Presentation",
        "SharedKernel.Integration",
        "SharedKernel.Testing",
        "SharedKernel.Workflows",
    ];

    /// <summary>
    /// Mirrors the fifteen-term forbidden list private to
    /// <see cref="SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts"/> — the
    /// symmetric swap of <see cref="ForbiddenTermsMirror"/>: <c>"SharedKernel.Search"</c> replaces
    /// <c>"SharedKernel.AI"</c>. Kept here, not reflected out of the production type, purely so
    /// T-231 can locate the array indices it needs without hard-coding fragile literal indices.
    /// </summary>
    private static readonly string[] IntelligenceForbiddenTermsMirror =
    [
        "SharedKernel.Caching",
        "SharedKernel.Domain",
        "SharedKernel.Application",
        "SharedKernel.Persistence",
        "SharedKernel.Messaging",
        "SharedKernel.Storage",
        "SharedKernel.Search",
        "SharedKernel.Communication",
        "SharedKernel.Security",
        "SharedKernel.ServiceDefaults",
        "SharedKernel.MultiTenancy",
        "SharedKernel.Presentation",
        "SharedKernel.Integration",
        "SharedKernel.Testing",
        "SharedKernel.Workflows",
    ];

    // ---------------------------------------------------------------------------
    // T-251 — Fire path: contrived 17.Workflows-shaped fixture references a stubbed forbidden-domain
    // type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-251: When a contrived "SharedKernel.Workflows"-shaped assembly references a type in a
    /// namespace simulating a forbidden capability domain (here, <c>SharedKernel.Persistence</c>),
    /// <see cref="SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication"/> must
    /// fail on the corresponding array element only.
    /// </summary>
    [Fact]
    public void WorkflowsReferencesOnlyCoreContractsAndApplication_ViolatingAssembly_CorrespondingElementFails()
    {
        const string violationSource = """
            namespace SharedKernel.Persistence
            {
                public interface IRepository { }
            }

            namespace SharedKernel.Workflows
            {
                public class LeakyWorkflowActivity
                {
                    private readonly SharedKernel.Persistence.IRepository _repository;
                    public LeakyWorkflowActivity(SharedKernel.Persistence.IRepository repository)
                    {
                        _repository = repository;
                    }
                }
            }
            """;

        var violationAssembly = CompileInMemory("ViolationWorkflows", violationSource);

        var conditionLists = SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication(violationAssembly);

        conditionLists.Should().HaveCount(14);

        var persistenceElementIndex = Array.IndexOf(WorkflowsForbiddenTermsMirror, "SharedKernel.Persistence");
        persistenceElementIndex.Should().BeGreaterThanOrEqualTo(0);

        for (var i = 0; i < conditionLists.Length; i++)
        {
            var result = conditionLists[i].GetResult();
            if (i == persistenceElementIndex)
            {
                result.IsSuccessful.Should().BeFalse(
                    because: "LeakyWorkflowActivity references SharedKernel.Persistence.IRepository directly");
            }
            else
            {
                result.IsSuccessful.Should().BeTrue(
                    because: "LeakyWorkflowActivity has no dependency on any other forbidden capability domain");
            }
        }
    }

    // ---------------------------------------------------------------------------
    // T-252 — Pass path: contrived 17.Workflows-shaped fixture references SharedKernel.Application —
    // proving it is PERMITTED, not forbidden — alongside SharedKernel.Primitives/SharedKernel.Contracts
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-252: A contrived "SharedKernel.Workflows"-shaped assembly referencing stubbed
    /// <c>SharedKernel.Primitives</c>/<c>SharedKernel.Contracts</c>/<c>SharedKernel.Application</c>
    /// types must pass every array element of
    /// <see cref="SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication"/> —
    /// proving <c>SharedKernel.Application</c> is PERMITTED, the distinguishing exclusion versus
    /// <see cref="SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts"/>'s and
    /// <see cref="SharedKernelLayeringRules.IntelligenceReferencesOnlyCoreAndContracts"/>'s own
    /// fifteen-term lists, both of which forbid it.
    /// </summary>
    [Fact]
    public void WorkflowsReferencesOnlyCoreContractsAndApplication_CleanAssemblyWithApplicationDependency_AllElementsPass()
    {
        const string cleanSource = """
            namespace SharedKernel.Primitives
            {
                public class Result { }
            }

            namespace SharedKernel.Contracts
            {
                public class PagedList { }
            }

            namespace SharedKernel.Application
            {
                public interface ISender { }
            }

            namespace SharedKernel.Workflows
            {
                public class ApproveOrderActivity
                {
                    private readonly SharedKernel.Application.ISender _sender;
                    public ApproveOrderActivity(SharedKernel.Application.ISender sender)
                    {
                        _sender = sender;
                    }

                    public SharedKernel.Primitives.Result DoWork() => new SharedKernel.Primitives.Result();
                    public SharedKernel.Contracts.PagedList ToPagedList() => new SharedKernel.Contracts.PagedList();
                }
            }
            """;

        var cleanAssembly = CompileInMemory("CleanWorkflows", cleanSource);

        var conditionLists = SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication(cleanAssembly);

        conditionLists.Should().HaveCount(14);
        foreach (var conditionList in conditionLists)
        {
            conditionList.GetResult().IsSuccessful.Should().BeTrue(
                because: "ApproveOrderActivity depends only on SharedKernel.Primitives, SharedKernel.Contracts, and the PERMITTED SharedKernel.Application — never any of the fourteen forbidden capability domains");
        }
    }

    // ---------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Mirrors the fourteen-term forbidden list private to
    /// <see cref="SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication"/> —
    /// kept here, not reflected out of the production type, purely so T-251 can locate the array
    /// index that corresponds to <c>"SharedKernel.Persistence"</c> without hard-coding a fragile
    /// literal index. Deliberately OMITS <c>"SharedKernel.Application"</c> — the distinguishing
    /// exclusion versus <see cref="ForbiddenTermsMirror"/>/<see cref="IntelligenceForbiddenTermsMirror"/>,
    /// both of which include it.
    /// </summary>
    private static readonly string[] WorkflowsForbiddenTermsMirror =
    [
        "SharedKernel.Caching",
        "SharedKernel.Domain",
        "SharedKernel.Persistence",
        "SharedKernel.Messaging",
        "SharedKernel.Storage",
        "SharedKernel.Search",
        "SharedKernel.AI",
        "SharedKernel.Communication",
        "SharedKernel.Security",
        "SharedKernel.ServiceDefaults",
        "SharedKernel.MultiTenancy",
        "SharedKernel.Presentation",
        "SharedKernel.Integration",
        "SharedKernel.Testing",
    ];

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles C# source code and emits it to a temp file, then loads the assembly from disk.
    /// NetArchTest uses Mono.Cecil which requires a physical file path — in-memory assemblies
    /// (loaded via <c>Assembly.Load(byte[])</c>) have an empty <see cref="Assembly.Location"/>
    /// and cannot be loaded by Mono.Cecil.
    /// </summary>
    /// <param name="assemblyName">Logical name for the compiled assembly.</param>
    /// <param name="source">C# source code to compile.</param>
    /// <returns>The loaded <see cref="Assembly"/> with a valid <see cref="Assembly.Location"/>.</returns>
    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Reference the minimal runtime assemblies required for compilation
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(
                Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        // Write to a temp file — NetArchTest / Mono.Cecil requires Assembly.Location to be set
        var tempPath = Path.Combine(Path.GetTempPath(), $"{assemblyName}_{Guid.NewGuid():N}.dll");
        using (var fs = File.OpenWrite(tempPath))
        {
            var emitResult = compilation.Emit(fs);

            if (!emitResult.Success)
            {
                var errors = string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                throw new InvalidOperationException(
                    $"Test fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
            }
        }

        return Assembly.LoadFrom(tempPath);
    }
}
