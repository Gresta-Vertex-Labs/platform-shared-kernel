using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="HealthCheckTagIntegrityRules"/> — introduced by WO-027 P-173.
/// </summary>
/// <remarks>
/// <para>
/// T-129/T-130 cover <see cref="HealthCheckTagIntegrityRules.NoConflictingLivenessReadinessTags"/>.
/// T-131/T-132/T-133 cover <see cref="HealthCheckTagIntegrityRules.DependencyHealthChecksCarryReadyNotLive"/>.
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
