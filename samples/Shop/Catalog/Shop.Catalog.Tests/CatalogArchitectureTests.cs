using System.Reflection;
using SharedKernel.ArchitectureTests.Helpers;
using SharedKernel.ArchitectureTests.Rules;
using Shop.Catalog.Api;
using Shop.Catalog.Application.Products;
using Shop.Catalog.Domain;
using Shop.Catalog.Infrastructure;
using Shop.TestSupport;
using Xunit;

namespace Shop.Catalog.Tests;

/// <summary>The Catalog's four-project shape, and the kernel's own architecture rules applied to its assemblies.</summary>
public sealed class CatalogArchitectureTests : ArchitectureRuleBase
{
    private static readonly ServiceShape Shape = new(
        DependencyGraph.Load("Shop.Catalog.Tests"),
        "Shop.Catalog"
    );

    private static readonly Assembly DomainAssembly = typeof(Product).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(CreateProductCommand).Assembly;
    private static readonly Assembly InfrastructureAssembly =
        typeof(CatalogInfrastructure).Assembly;
    private static readonly Assembly ApiAssembly = typeof(CatalogEndpoints).Assembly;

    [Fact]
    public void ProjectReferences_FollowTheLayering() => Shape.ProjectReferencesFollowTheLayering();

    [Fact]
    public void Domain_SeesFoundationAndModelOnly() => Shape.DomainSeesFoundationAndModelOnly();

    [Fact]
    public void Application_SeesContractsOnly() => Shape.ApplicationSeesContractsOnly();

    [Fact]
    public void Infrastructure_SeesAdaptersButNoHost() =>
        Shape.InfrastructureSeesAdaptersButNoHost();

    [Fact]
    public void Api_IsTheCompositionRoot() => Shape.HostIsTheCompositionRoot();

    [Fact]
    public void Production_NeverReferencesTestingPackages() =>
        Shape.ProductionNeverReferencesTestingPackages();

    [Fact]
    public void EveryKernelPackage_IsKnown() => Shape.EveryKernelPackageIsKnown();

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
