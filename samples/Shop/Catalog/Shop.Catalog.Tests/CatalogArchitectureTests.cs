using System.Reflection;
using SharedKernel.ArchitectureTests.Helpers;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.ArchitectureTests.ServiceShape;
using Shop.Catalog.Api;
using Shop.Catalog.Application.Products;
using Shop.Catalog.Domain;
using Shop.Catalog.Infrastructure;
using Xunit;

namespace Shop.Catalog.Tests;

/// <summary>The Catalog's four-project shape, and the kernel's own architecture rules applied to its assemblies.</summary>
public sealed class CatalogArchitectureTests : ArchitectureRuleBase
{
    private static readonly ServiceDependencyGraph Graph = ServiceDependencyGraph.Load(
        "Shop.Catalog.Tests"
    );
    private static readonly ServiceShapeOptions Shape = new("Shop.Catalog");

    private static readonly Assembly DomainAssembly = typeof(Product).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(CreateProductCommand).Assembly;
    private static readonly Assembly InfrastructureAssembly =
        typeof(CatalogInfrastructure).Assembly;
    private static readonly Assembly ApiAssembly = typeof(CatalogEndpoints).Assembly;

    [Fact]
    public void ProjectReferences_FollowTheLayering() =>
        ServiceShapeRules.AssertProjectReferencesFollowTheLayering(Graph, Shape);

    [Fact]
    public void Domain_SeesFoundationAndModelOnly() =>
        ServiceShapeRules.AssertDomainSeesFoundationAndModelOnly(Graph, Shape);

    [Fact]
    public void Application_SeesContractsOnly() =>
        ServiceShapeRules.AssertApplicationSeesContractsOnly(Graph, Shape);

    [Fact]
    public void Infrastructure_SeesAdaptersButNoHost() =>
        ServiceShapeRules.AssertInfrastructureSeesAdaptersButNoHost(Graph, Shape);

    [Fact]
    public void Api_IsTheCompositionRoot() =>
        ServiceShapeRules.AssertHostIsTheCompositionRoot(Graph, Shape);

    [Fact]
    public void Production_NeverReferencesTestingPackages() =>
        ServiceShapeRules.AssertProductionNeverReferencesTestingPackages(Graph, Shape);

    [Fact]
    public void EveryKernelPackage_IsKnown() =>
        ServiceShapeRules.AssertEveryKernelPackageIsKnown(Graph, Shape);

    [Fact]
    public void Nothing_ReferencesTheApi() =>
        ServiceShapeRules.AssertNothingReferencesTheHost(Graph, Shape);

    private static readonly Assembly[] ServiceAssemblies =
    [
        DomainAssembly,
        ApplicationAssembly,
        InfrastructureAssembly,
        ApiAssembly,
    ];

    // The placement rules the Catalog already satisfies. Its use cases and domain types sit in feature folders rather than
    // one namespace per use case, so the use-case, domain-feature, event-handler, configuration and endpoint-module
    // rules are not applied here.
    private static readonly ServicePlacementOptions Placement = new("Shop.Catalog");

    [Fact]
    public void MiddlewareAndFilters_LiveInTheirFolders() =>
        AssertRule(
            ServicePlacementRules.MiddlewareAndFiltersLiveInTheirFolders(
                ServiceAssemblies,
                Placement
            )
        );

    [Fact]
    public void NoNamespace_UsesAGenericFolderName() =>
        AssertRule(ServicePlacementRules.NoGenericFolderNames(ServiceAssemblies, Placement));

    [Fact]
    public void NoType_DerivesFromException() =>
        AssertRule(ServicePlacementRules.NoExceptionTypes(ServiceAssemblies, Placement));

    [Fact]
    public void Domain_NeverReadsTheSystemClock() =>
        AssertRule(DomainLayerPurityRules.DomainAssembliesNeverCallSystemClock(DomainAssembly));

    [Fact]
    public void Domain_NeverReferencesInfrastructure() =>
        AssertRule(
            DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure(DomainAssembly)
        );

    [Fact]
    public void Domain_NeverReferencesThePersistenceStack() =>
        AssertRule(
            PersistenceLayerProtectionRules.DomainAssembliesNeverReferencePersistenceStack(
                DomainAssembly
            )
        );

    [Fact]
    public void Domain_HasNoEventHandlers() =>
        AssertRule(
            DomainLayerPurityRules.DomainAssembliesNeverContainEventHandlers(DomainAssembly)
        );

    [Fact]
    public void Application_NeverTouchesADatabaseTransaction() =>
        AssertRule(
            EfCorePackageHygieneRules.ApplicationLayerMustNotReferenceDbContextTransaction(
                ApplicationAssembly
            )
        );

    [Fact]
    public void NoRawCipher_InDomainOrApplication() =>
        AssertRule(
            EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication(
                DomainAssembly,
                ApplicationAssembly
            )
        );

    [Fact]
    public void ProblemDetails_AreOnlyBuiltByTheWebApiPackage() =>
        AssertRule(
            PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi(
                ApiAssembly,
                InfrastructureAssembly
            )
        );
}
