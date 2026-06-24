using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="HealthCheckTagIntegrityRules"/> and
/// <see cref="CompositionRootExclusivityRules"/> — introduced by WO-027 P-173.
/// </summary>
/// <remarks>
/// <para>
/// T-129/T-130 cover <see cref="HealthCheckTagIntegrityRules.NoConflictingLivenessReadinessTags"/>.
/// T-131/T-132/T-133 cover <see cref="HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive"/>.
/// T-134/T-135/T-136/T-137/T-138 cover
/// <see cref="CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders"/>.
/// </para>
/// <para>
/// Per Implementation Rule 8 of the <c>SK.00.ServiceDefaultsGovernance</c> phase spec,
/// <c>SharedKernel.ServiceDefaults</c> and <c>SharedKernel.MultiTenancy</c> do not exist as
/// buildable assemblies until P-170 ships. All fixtures here are contrived in-memory
/// assemblies built via <see cref="CSharpCompilation"/> +
/// <see cref="MetadataReference.CreateFromImage(System.Collections.Immutable.ImmutableArray{byte})"/>,
/// the same technique documented for <c>RedisTopologyRulesTests</c>.
/// </para>
/// </remarks>
public class ServiceDefaultsGovernanceRulesTests
{
    // ---------------------------------------------------------------------------
    // T-129 — Fire path: a single registration carries both "live" and "ready"
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-129: A contrived fixture method shaped as <c>AddDatabaseReadinessCheck</c> that
    /// pushes both <c>"live"</c> and <c>"ready"</c> string literals into its tag array must
    /// fail <see cref="HealthCheckTagIntegrityRules.NoConflictingLivenessReadinessTags"/>,
    /// naming the offending type.
    /// </summary>
    [Fact]
    public void NoConflictingLivenessReadinessTags_ConflictingTags_RuleFails()
    {
        const string source = """
            namespace Fixture.ServiceDefaults
            {
                public static class HealthCheckExtensions
                {
                    public static void AddDatabaseReadinessCheck()
                    {
                        var tags = new[] { "live", "ready" };
                        System.Console.WriteLine(tags.Length);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.HealthCheckTags.Conflicting", source);

        var result = HealthCheckTagIntegrityRules
            .NoConflictingLivenessReadinessTags(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "AddDatabaseReadinessCheck carries both \"live\" and \"ready\" simultaneously");

        result.FailingTypeNames.Should().Contain(
            "Fixture.ServiceDefaults.HealthCheckExtensions",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-130 — Pass path: every registration carries exactly one of live/ready
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-130: A contrived fixture assembly where every <c>Add*HealthCheck</c>/
    /// <c>Add*ReadinessCheck</c> method carries exactly one of <c>"live"</c>/<c>"ready"</c>
    /// must pass <see cref="HealthCheckTagIntegrityRules.NoConflictingLivenessReadinessTags"/>.
    /// </summary>
    [Fact]
    public void NoConflictingLivenessReadinessTags_NonConflictingTags_RulePasses()
    {
        const string source = """
            namespace Fixture.ServiceDefaults
            {
                public static class HealthCheckExtensions
                {
                    public static void AddStartupHealthCheck()
                    {
                        var tags = new[] { "live" };
                        System.Console.WriteLine(tags.Length);
                    }

                    public static void AddDatabaseReadinessCheck()
                    {
                        var tags = new[] { "ready" };
                        System.Console.WriteLine(tags.Length);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.HealthCheckTags.NonConflicting", source);

        var result = HealthCheckTagIntegrityRules
            .NoConflictingLivenessReadinessTags(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "each registration method carries exactly one of \"live\"/\"ready\"");
    }

    // ---------------------------------------------------------------------------
    // T-131 — Fire path: dependency check missing "ready" fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-131: A contrived <c>AddRedisHealthCheck</c>-shaped fixture method whose tag literal
    /// set is <c>{"ready"}</c> passes, while a sibling <c>AddRabbitMqHealthCheck</c> method
    /// whose tag set is empty (no <c>"ready"</c>) fails
    /// <see cref="HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive"/>.
    /// </summary>
    [Fact]
    public void DependencyHealthChecksCarryReadyNotLive_MissingReadyTag_RuleFails()
    {
        const string source = """
            namespace Fixture.ServiceDefaults
            {
                public static class HealthCheckExtensions
                {
                    public static void AddRedisHealthCheck()
                    {
                        var tags = new[] { "ready" };
                        System.Console.WriteLine(tags.Length);
                    }

                    public static void AddRabbitMqHealthCheck()
                    {
                        var tags = new string[0];
                        System.Console.WriteLine(tags.Length);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.HealthCheckTags.MissingReady", source);

        var result = HealthCheckTagIntegrityRules
            .DependencyHealthChecksCarryReadyNotLive(
                assembly,
                "AddRedis",
                "AddRabbitMq")
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "AddRabbitMqHealthCheck does not carry the required \"ready\" tag");

        result.FailingTypeNames.Should().Contain(
            "Fixture.ServiceDefaults.HealthCheckExtensions",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-132 — Fire path: dependency check carrying "live" fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-132: A contrived <c>AddAzureServiceBusHealthCheck</c>-shaped fixture method whose tag
    /// literal set is <c>{"live"}</c> must fail
    /// <see cref="HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive"/>,
    /// naming the offending type.
    /// </summary>
    [Fact]
    public void DependencyHealthChecksCarryReadyNotLive_CarriesLiveTag_RuleFails()
    {
        const string source = """
            namespace Fixture.ServiceDefaults
            {
                public static class HealthCheckExtensions
                {
                    public static void AddAzureServiceBusHealthCheck()
                    {
                        var tags = new[] { "live" };
                        System.Console.WriteLine(tags.Length);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.HealthCheckTags.CarriesLive", source);

        var result = HealthCheckTagIntegrityRules
            .DependencyHealthChecksCarryReadyNotLive(assembly, "AddAzureServiceBus")
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "AddAzureServiceBusHealthCheck carries the forbidden \"live\" tag");

        result.FailingTypeNames.Should().Contain(
            "Fixture.ServiceDefaults.HealthCheckExtensions",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-133 — Pass path: dependency check carrying only "ready"
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-133: A contrived <c>AddCacheHealthCheck</c>-shaped fixture method whose tag literal
    /// set is <c>{"ready"}</c> only must pass
    /// <see cref="HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive"/>.
    /// </summary>
    [Fact]
    public void DependencyHealthChecksCarryReadyNotLive_OnlyReadyTag_RulePasses()
    {
        const string source = """
            namespace Fixture.ServiceDefaults
            {
                public static class HealthCheckExtensions
                {
                    public static void AddCacheHealthCheck()
                    {
                        var tags = new[] { "ready" };
                        System.Console.WriteLine(tags.Length);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.HealthCheckTags.OnlyReady", source);

        var result = HealthCheckTagIntegrityRules
            .DependencyHealthChecksCarryReadyNotLive(assembly, "AddCache")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "AddCacheHealthCheck carries only the required \"ready\" tag");
    }

    // ---------------------------------------------------------------------------
    // T-134 — Fire path: non-exempt assembly references SharedKernel.Persistence.EfCore
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-134: A contrived non-exempt assembly (shaped as <c>05.Application</c>) containing a
    /// type that references <c>SharedKernel.Persistence.EfCore</c> must fail
    /// <see cref="CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders"/>
    /// on the <c>SharedKernel.Persistence.EfCore</c> element, naming the offending type.
    /// </summary>
    [Fact]
    public void OnlyAllowedAssembliesMayReferenceConcreteProviders_EfCoreReference_RuleFails()
    {
        const string efCoreSource = """
            namespace SharedKernel.Persistence.EfCore
            {
                public interface IUnitOfWork { }
            }
            """;

        const string applicationSource = """
            namespace Fixture.Application
            {
                public class OrderCommandHandler
                {
                    private readonly SharedKernel.Persistence.EfCore.IUnitOfWork _unitOfWork;
                    public OrderCommandHandler(SharedKernel.Persistence.EfCore.IUnitOfWork unitOfWork)
                    {
                        _unitOfWork = unitOfWork;
                    }
                }
            }
            """;

        var efCoreAssembly = CompileInMemory(
            "Fixture.SharedKernel.Persistence.EfCore",
            efCoreSource);
        var applicationAssembly = CompileInMemory(
            "ViolatingFixture.Application",
            applicationSource,
            extraReferences: new[] { efCoreAssembly });

        var conditionLists = CompositionRootExclusivityRules
            .OnlyAllowedAssembliesMayReferenceConcreteProviders(applicationAssembly);

        var efCoreResult = conditionLists[0].GetResult();

        efCoreResult.IsSuccessful.Should().BeFalse(
            because: "OrderCommandHandler references SharedKernel.Persistence.EfCore directly");

        efCoreResult.FailingTypeNames.Should().Contain(
            "Fixture.Application.OrderCommandHandler",
            because: "the failure must name the offending type");
    }

    // ---------------------------------------------------------------------------
    // T-135 — Fire path: non-exempt assembly references SharedKernel.Messaging.MassTransit
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-135: A contrived non-exempt assembly containing a type that references
    /// <c>SharedKernel.Messaging.MassTransit</c> must fail the corresponding
    /// <see cref="ConditionList"/> element.
    /// </summary>
    [Fact]
    public void OnlyAllowedAssembliesMayReferenceConcreteProviders_MassTransitReference_RuleFails()
    {
        const string massTransitSource = """
            namespace SharedKernel.Messaging.MassTransit
            {
                public interface IConsumerDefinition { }
            }
            """;

        const string applicationSource = """
            namespace Fixture.Application
            {
                public class OrderConsumerDefinition
                {
                    private readonly SharedKernel.Messaging.MassTransit.IConsumerDefinition _definition;
                    public OrderConsumerDefinition(SharedKernel.Messaging.MassTransit.IConsumerDefinition definition)
                    {
                        _definition = definition;
                    }
                }
            }
            """;

        var massTransitAssembly = CompileInMemory(
            "Fixture.SharedKernel.Messaging.MassTransit",
            massTransitSource);
        var applicationAssembly = CompileInMemory(
            "ViolatingFixture.Application.MassTransit",
            applicationSource,
            extraReferences: new[] { massTransitAssembly });

        var conditionLists = CompositionRootExclusivityRules
            .OnlyAllowedAssembliesMayReferenceConcreteProviders(applicationAssembly);

        var massTransitResult = conditionLists[3].GetResult();

        massTransitResult.IsSuccessful.Should().BeFalse(
            because: "OrderConsumerDefinition references SharedKernel.Messaging.MassTransit directly");
    }

    // ---------------------------------------------------------------------------
    // T-136 — Fire path: non-exempt assembly references SharedKernel.Security.Oidc
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-136: A contrived non-exempt assembly containing a type that references
    /// <c>SharedKernel.Security.Oidc</c> must fail the corresponding
    /// <see cref="ConditionList"/> element.
    /// </summary>
    [Fact]
    public void OnlyAllowedAssembliesMayReferenceConcreteProviders_OidcReference_RuleFails()
    {
        const string oidcSource = """
            namespace SharedKernel.Security.Oidc
            {
                public interface IOidcTokenValidator { }
            }
            """;

        const string applicationSource = """
            namespace Fixture.Application
            {
                public class TokenValidationHandler
                {
                    private readonly SharedKernel.Security.Oidc.IOidcTokenValidator _validator;
                    public TokenValidationHandler(SharedKernel.Security.Oidc.IOidcTokenValidator validator)
                    {
                        _validator = validator;
                    }
                }
            }
            """;

        var oidcAssembly = CompileInMemory(
            "Fixture.SharedKernel.Security.Oidc",
            oidcSource);
        var applicationAssembly = CompileInMemory(
            "ViolatingFixture.Application.Oidc",
            applicationSource,
            extraReferences: new[] { oidcAssembly });

        var conditionLists = CompositionRootExclusivityRules
            .OnlyAllowedAssembliesMayReferenceConcreteProviders(applicationAssembly);

        var oidcResult = conditionLists[4].GetResult();

        oidcResult.IsSuccessful.Should().BeFalse(
            because: "TokenValidationHandler references SharedKernel.Security.Oidc directly");
    }

    // ---------------------------------------------------------------------------
    // T-137 — Pass path: composition-root-shaped assemblies are exempt by caller discipline
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-137: Contrived fixture assemblies shaped as <c>SharedKernel.ServiceDefaults</c> and
    /// <c>SharedKernel.MultiTenancy</c> that DO reference the five forbidden provider terms
    /// must still pass every <see cref="ConditionList"/> element when the caller does not
    /// include them in the scanned set — confirming the composition-root exemption holds by
    /// caller discipline (the rule is assembly-parameterized; the caller, not the rule, omits
    /// exempt assemblies, mirroring <see cref="CachingAbstractionRules"/>).
    /// </summary>
    [Fact]
    public void OnlyAllowedAssembliesMayReferenceConcreteProviders_CompositionRootAssemblies_RulePasses()
    {
        const string efCoreSource = """
            namespace SharedKernel.Persistence.EfCore
            {
                public interface IUnitOfWork { }
            }
            """;

        const string massTransitSource = """
            namespace SharedKernel.Messaging.MassTransit
            {
                public interface IConsumerDefinition { }
            }
            """;

        const string oidcSource = """
            namespace SharedKernel.Security.Oidc
            {
                public interface IOidcTokenValidator { }
            }
            """;

        const string serviceDefaultsSource = """
            namespace SharedKernel.ServiceDefaults
            {
                public class CompositionRoot
                {
                    private readonly SharedKernel.Persistence.EfCore.IUnitOfWork _unitOfWork;
                    private readonly SharedKernel.Messaging.MassTransit.IConsumerDefinition _consumer;
                    private readonly SharedKernel.Security.Oidc.IOidcTokenValidator _validator;

                    public CompositionRoot(
                        SharedKernel.Persistence.EfCore.IUnitOfWork unitOfWork,
                        SharedKernel.Messaging.MassTransit.IConsumerDefinition consumer,
                        SharedKernel.Security.Oidc.IOidcTokenValidator validator)
                    {
                        _unitOfWork = unitOfWork;
                        _consumer = consumer;
                        _validator = validator;
                    }
                }
            }
            """;

        const string multiTenancySource = """
            namespace SharedKernel.MultiTenancy
            {
                public class TenantCompositionRoot
                {
                    private readonly SharedKernel.Persistence.EfCore.IUnitOfWork _unitOfWork;
                    public TenantCompositionRoot(SharedKernel.Persistence.EfCore.IUnitOfWork unitOfWork)
                    {
                        _unitOfWork = unitOfWork;
                    }
                }
            }
            """;

        var efCoreAssembly = CompileInMemory("Fixture.T137.SharedKernel.Persistence.EfCore", efCoreSource);
        var massTransitAssembly = CompileInMemory("Fixture.T137.SharedKernel.Messaging.MassTransit", massTransitSource);
        var oidcAssembly = CompileInMemory("Fixture.T137.SharedKernel.Security.Oidc", oidcSource);

        var serviceDefaultsAssembly = CompileInMemory(
            "Fixture.SharedKernel.ServiceDefaults",
            serviceDefaultsSource,
            extraReferences: new[] { efCoreAssembly, massTransitAssembly, oidcAssembly });

        var multiTenancyAssembly = CompileInMemory(
            "Fixture.SharedKernel.MultiTenancy",
            multiTenancySource,
            extraReferences: new[] { efCoreAssembly });

        // The composition-root exemption holds because the caller — exactly as the phase spec
        // requires — never passes the exempt assemblies into the scanned set.
        var nonExemptOnly = Array.Empty<Assembly>();

        var conditionLists = CompositionRootExclusivityRules
            .OnlyAllowedAssembliesMayReferenceConcreteProviders(nonExemptOnly);

        foreach (var conditionList in conditionLists)
        {
            // An empty scanned-assembly set trivially has no failing types.
            var result = conditionList.GetResult();
            result.IsSuccessful.Should().BeTrue(
                because: "no assemblies were scanned — the composition-root assemblies were correctly excluded by the caller");
        }

        // Sanity: directly scanning the composition-root-shaped assemblies WOULD fail,
        // proving the exemption is a caller-discipline contract, not a false negative in the rule.
        var directScan = CompositionRootExclusivityRules
            .OnlyAllowedAssembliesMayReferenceConcreteProviders(serviceDefaultsAssembly, multiTenancyAssembly);

        directScan[0].GetResult().IsSuccessful.Should().BeFalse(
            because: "composition-root assemblies legitimately reference concrete providers — they must never be passed to this rule");
    }

    // ---------------------------------------------------------------------------
    // T-138 — Pass path: non-exempt assembly referencing none of the five forbidden terms
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-138: A contrived non-exempt assembly referencing none of the five forbidden terms
    /// (only <c>SharedKernel.Core</c>/<c>SharedKernel.Contracts</c>-shaped namespaces) must
    /// pass every element of the returned <see cref="ConditionList"/> array.
    /// </summary>
    [Fact]
    public void OnlyAllowedAssembliesMayReferenceConcreteProviders_NoForbiddenReferences_RulePasses()
    {
        const string coreSource = """
            namespace SharedKernel.Core
            {
                public static class StringExtensions
                {
                    public static bool IsNullOrWhiteSpaceSafe(string? value) => string.IsNullOrWhiteSpace(value);
                }
            }
            """;

        const string contractsSource = """
            namespace SharedKernel.Contracts
            {
                public record OrderSummaryDto(System.Guid Id, string Status);
            }
            """;

        const string applicationSource = """
            namespace Fixture.Application
            {
                public class OrderSummaryProjector
                {
                    public SharedKernel.Contracts.OrderSummaryDto Project(System.Guid id, string status)
                    {
                        SharedKernel.Core.StringExtensions.IsNullOrWhiteSpaceSafe(status);
                        return new SharedKernel.Contracts.OrderSummaryDto(id, status);
                    }
                }
            }
            """;

        var coreAssembly = CompileInMemory("Fixture.T138.SharedKernel.Core", coreSource);
        var contractsAssembly = CompileInMemory("Fixture.T138.SharedKernel.Contracts", contractsSource);
        var applicationAssembly = CompileInMemory(
            "CompliantFixture.Application",
            applicationSource,
            extraReferences: new[] { coreAssembly, contractsAssembly });

        var conditionLists = CompositionRootExclusivityRules
            .OnlyAllowedAssembliesMayReferenceConcreteProviders(applicationAssembly);

        foreach (var conditionList in conditionLists)
        {
            var result = conditionList.GetResult();
            result.IsSuccessful.Should().BeTrue(
                because: "OrderSummaryProjector references no forbidden provider term");
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and loads it for
    /// reflection. Follows the established pattern from <c>RedisTopologyRulesTests</c> and
    /// <c>ReflectionGuardRulesTests</c>.
    /// </summary>
    private static Assembly CompileInMemory(
        string assemblyName,
        string source,
        Assembly[]? extraReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Console").Location),
        };

        if (extraReferences is not null)
        {
            foreach (var extraReference in extraReferences)
            {
                references.Add(
                    MetadataReference.CreateFromImage(
                        System.Collections.Immutable.ImmutableArray.Create(
                            File.ReadAllBytes(extraReference.Location))));
            }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{assemblyName}_{Guid.NewGuid():N}.dll");

        using var stream = new MemoryStream();

        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));

            throw new InvalidOperationException(
                $"Fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
        }

        stream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(tempPath, stream.ToArray());

        return Assembly.LoadFrom(tempPath);
    }
}
