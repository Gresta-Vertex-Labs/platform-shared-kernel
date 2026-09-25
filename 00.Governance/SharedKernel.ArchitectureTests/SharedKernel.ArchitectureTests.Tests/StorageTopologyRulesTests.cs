using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="StorageTopologyRules"/> — the <c>08.Storage</c> package topology
/// enforcement predicates introduced by WO-043 P-271 (Obs building on S3 since the named-store redesign).
/// </summary>
/// <remarks>
/// <para>
/// T-194/T-195 cover <see cref="StorageTopologyRules.AbstractionsHasNoThirdPartyDependencies"/>, with
/// <see cref="StorageTopologyRules.AbstractionsForbiddenAssemblyReferences"/> as its assembly-level half.
/// <see cref="StorageTopologyRules.S3NeverReferencesObs"/>/<see cref="StorageTopologyRules.S3ForbiddenAssemblyReferences"/> replace the former sibling rule: <c>.Obs</c> now builds on <c>.S3</c>.
/// T-198/T-199 cover <see cref="StorageTopologyRules.OnlyProviderPackagesMayReferenceAmazonS3"/>.
/// </para>
/// <para>
/// Every fire-path/pass-path pair uses CONTRIVED in-memory fixture assemblies — the primary
/// red/green proof this phase specifies, per the "designed-ahead-of-a-pending-dependency"
/// precedent this domain has followed since <c>RedisTopologyRulesTests</c>. Additional
/// <c>Real*</c>-suffixed tests below re-run the same three predicates against the REAL, now
/// Published <c>SharedKernel.Storage.Abstractions</c>/<c>.S3</c>/<c>.Obs</c> assemblies — the
/// dependency the phase spec originally flagged as blocking (P-265/P-266/P-267) resolved to
/// <c>●</c> Complete before this phase's implementation session, so real-assembly verification is
/// wired now rather than deferred as a follow-up.
/// </para>
/// </remarks>
public class StorageTopologyRulesTests
{
    // ---------------------------------------------------------------------------
    // T-194 — Fire path: contrived Abstractions-shaped fixture depends on stubbed Amazon.*
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-194: When a contrived "SharedKernel.Storage.Abstractions"-shaped assembly references a
    /// type whose declaring assembly simulates the <c>Amazon.S3</c> namespace,
    /// <see cref="StorageTopologyRules.AbstractionsHasNoThirdPartyDependencies"/> must fail.
    /// </summary>
    [Fact]
    public void AbstractionsHasNoThirdPartyDependencies_ViolatingAssembly_RuleFails()
    {
        const string amazonStubSource = """
            namespace Amazon.S3
            {
                public interface IAmazonS3 { }
            }
            """;

        const string abstractionsSource = """
            namespace SharedKernel.Storage
            {
                public class LeakyFileStorage
                {
                    private readonly Amazon.S3.IAmazonS3 _client;
                    public LeakyFileStorage(Amazon.S3.IAmazonS3 client)
                    {
                        _client = client;
                    }
                }
            }
            """;

        var amazonStubAssembly = CompileInMemory("Fixture.StorageAbstractionsTest.Amazon.S3", amazonStubSource);
        var abstractionsAssembly = CompileInMemory(
            "ViolatingSharedKernel.Storage.Abstractions",
            abstractionsSource,
            extraReferences: new[] { amazonStubAssembly });

        var conditionList = StorageTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LeakyFileStorage references the Amazon.S3 namespace directly");
    }

    // ---------------------------------------------------------------------------
    // T-195 — Pass path: contrived Abstractions-shaped fixture depends only on stubbed Primitives
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-195: A contrived "SharedKernel.Storage.Abstractions"-shaped assembly depending only on a
    /// stubbed <c>SharedKernel.Primitives</c> type must pass
    /// <see cref="StorageTopologyRules.AbstractionsHasNoThirdPartyDependencies"/>.
    /// </summary>
    [Fact]
    public void AbstractionsHasNoThirdPartyDependencies_CleanAssembly_RulePasses()
    {
        const string primitivesStubSource = """
            namespace SharedKernel.Primitives
            {
                public class Result { }
            }
            """;

        const string abstractionsSource = """
            namespace SharedKernel.Storage
            {
                public class CleanFileStorage
                {
                    public SharedKernel.Primitives.Result DoWork() => new SharedKernel.Primitives.Result();
                }
            }
            """;

        var primitivesStubAssembly = CompileInMemory(
            "Fixture.StorageAbstractionsCleanTest.SharedKernel.Primitives",
            primitivesStubSource);
        var abstractionsAssembly = CompileInMemory(
            "CleanSharedKernel.Storage.Abstractions",
            abstractionsSource,
            extraReferences: new[] { primitivesStubAssembly });

        var conditionList = StorageTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "CleanFileStorage depends only on SharedKernel.Primitives, never Amazon/S3/Obs/Configuration");
    }

    // ---------------------------------------------------------------------------
    // Abstractions — assembly-reference half
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A contrived abstractions-shaped assembly that uses a provider type declared in the shared
    /// <c>SharedKernel.Storage</c> namespace passes the namespace check but is caught by
    /// <see cref="StorageTopologyRules.AbstractionsForbiddenAssemblyReferences"/> — the gap the
    /// assembly-reference half exists to close.
    /// </summary>
    [Fact]
    public void AbstractionsForbiddenAssemblyReferences_ProviderTypeInSharedNamespace_IsReported()
    {
        const string s3StubSource = """
            namespace SharedKernel.Storage
            {
                public static class S3StorageBuilderExtensions { public const string S3ConnectionName = "S3"; public static string Name() => S3ConnectionName; }
            }
            """;

        const string abstractionsSource = """
            namespace SharedKernel.Storage
            {
                public class LeakyRegistry
                {
                    public string Connection() => SharedKernel.Storage.S3StorageBuilderExtensions.Name();
                }
            }
            """;

        var s3StubImage = CompileImage("SharedKernel.Storage.S3", s3StubSource);
        var abstractionsAssembly = LoadImage(
            "ViolatingSharedKernel.Storage.Abstractions.SharedNamespace",
            CompileImage("ViolatingSharedKernel.Storage.Abstractions.SharedNamespace", abstractionsSource, s3StubImage));

        StorageTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly).GetResult().IsSuccessful
            .Should().BeTrue(because: "the provider type lives in the shared SharedKernel.Storage namespace, invisible to a namespace check");
        StorageTopologyRules.AbstractionsForbiddenAssemblyReferences(abstractionsAssembly)
            .Should().Equal(["SharedKernel.Storage.S3"], because: "the referenced assembly name still reveals the provider dependency");
    }

    // ---------------------------------------------------------------------------
    // S3 never references Obs — Obs is a thin provider over S3, never the reverse
    // ---------------------------------------------------------------------------

    /// <summary>
    /// When a contrived "SharedKernel.Storage.S3"-shaped assembly references a type in the
    /// <c>SharedKernel.Storage.Obs</c> namespace, <see cref="StorageTopologyRules.S3NeverReferencesObs"/>
    /// must fail.
    /// </summary>
    [Fact]
    public void S3NeverReferencesObs_S3ReferencesObsNamespace_RuleFails()
    {
        const string obsStubSource = """
            namespace SharedKernel.Storage.Obs
            {
                public sealed class ObsStorageOptions { }
            }
            """;

        const string s3Source = """
            namespace SharedKernel.Storage.S3
            {
                public class LeakyS3FileStorage
                {
                    private readonly SharedKernel.Storage.Obs.ObsStorageOptions _obsOptions;
                    public LeakyS3FileStorage(SharedKernel.Storage.Obs.ObsStorageOptions obsOptions)
                    {
                        _obsOptions = obsOptions;
                    }
                }
            }
            """;

        var obsStubAssembly = CompileInMemory("Fixture.S3ToObsTest.SharedKernel.Storage.Obs", obsStubSource);
        var violatingS3Assembly = CompileInMemory(
            "ViolatingSharedKernel.Storage.S3",
            s3Source,
            extraReferences: new[] { obsStubAssembly });

        var result = StorageTopologyRules.S3NeverReferencesObs(violatingS3Assembly).GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LeakyS3FileStorage special-cases OBS inside the S3 implementation");
    }

    /// <summary>
    /// An "SharedKernel.Storage.S3"-shaped assembly calling an OBS entry point declared in the shared
    /// <c>SharedKernel.Storage</c> namespace slips past the namespace check but is reported by
    /// <see cref="StorageTopologyRules.S3ForbiddenAssemblyReferences"/>.
    /// </summary>
    [Fact]
    public void S3ForbiddenAssemblyReferences_S3UsesObsEntryPointInSharedNamespace_IsReported()
    {
        const string obsStubSource = """
            namespace SharedKernel.Storage
            {
                public static class ObsStorageBuilderExtensions { public const string ObsConnectionName = "Obs"; public static string Name() => ObsConnectionName; }
            }
            """;

        const string s3Source = """
            namespace SharedKernel.Storage.S3
            {
                public class LeakyS3Registration
                {
                    public string Connection() => SharedKernel.Storage.ObsStorageBuilderExtensions.Name();
                }
            }
            """;

        var obsStubImage = CompileImage("SharedKernel.Storage.Obs", obsStubSource);
        var violatingS3Assembly = LoadImage(
            "ViolatingSharedKernel.Storage.S3.SharedNamespace",
            CompileImage("ViolatingSharedKernel.Storage.S3.SharedNamespace", s3Source, obsStubImage));

        StorageTopologyRules.S3NeverReferencesObs(violatingS3Assembly).GetResult().IsSuccessful
            .Should().BeTrue(because: "AddObs-style entry points live in the shared SharedKernel.Storage namespace");
        StorageTopologyRules.S3ForbiddenAssemblyReferences(violatingS3Assembly)
            .Should().Equal(["SharedKernel.Storage.Obs"], because: "the S3 package must never reference the OBS package");
    }

    /// <summary>
    /// The permitted direction: an "SharedKernel.Storage.S3"-shaped assembly with no OBS dependency
    /// passes both halves, while an Obs-shaped assembly may freely build on S3 — no rule constrains it.
    /// </summary>
    [Fact]
    public void S3NeverReferencesObs_CleanS3_BothHalvesPass()
    {
        const string s3Source = """
            namespace SharedKernel.Storage.S3
            {
                public class CleanS3FileStorage { }
            }
            """;

        var s3Assembly = CompileInMemory("CleanSharedKernel.Storage.S3.NoObs", s3Source);

        StorageTopologyRules.S3NeverReferencesObs(s3Assembly).GetResult().IsSuccessful
            .Should().BeTrue(because: "the S3 fixture has no dependency on OBS");
        StorageTopologyRules.S3ForbiddenAssemblyReferences(s3Assembly)
            .Should().BeEmpty(because: "the S3 fixture references no OBS assembly");
    }

    // ---------------------------------------------------------------------------
    // T-198 — Fire path: non-provider consumer references stubbed Amazon.S3
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-198: When a contrived non-provider consumer assembly references a type whose declaring
    /// assembly simulates <c>Amazon.S3</c>,
    /// <see cref="StorageTopologyRules.OnlyProviderPackagesMayReferenceAmazonS3"/> must fail.
    /// </summary>
    [Fact]
    public void OnlyProviderPackagesMayReferenceAmazonS3_ViolatingConsumer_RuleFails()
    {
        const string amazonStubSource = """
            namespace Amazon.S3
            {
                public interface IAmazonS3 { }
            }
            """;

        const string consumerSource = """
            namespace SomeConsumingService.Handlers
            {
                public class UploadDocumentHandler
                {
                    private readonly Amazon.S3.IAmazonS3 _client;
                    public UploadDocumentHandler(Amazon.S3.IAmazonS3 client)
                    {
                        _client = client;
                    }
                }
            }
            """;

        var amazonStubAssembly = CompileInMemory("Fixture.ConsumerLeakTest.Amazon.S3", amazonStubSource);
        var consumerAssembly = CompileInMemory(
            "ViolatingConsumer",
            consumerSource,
            extraReferences: new[] { amazonStubAssembly });

        var conditionList = StorageTopologyRules.OnlyProviderPackagesMayReferenceAmazonS3(consumerAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "UploadDocumentHandler references Amazon.S3.IAmazonS3 directly instead of IFileStorage");
    }

    // ---------------------------------------------------------------------------
    // T-199 — Pass path: non-provider consumer references only stubbed Abstractions
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-199: A contrived non-provider consumer assembly referencing only a stubbed
    /// <c>SharedKernel.Storage.Abstractions</c> type must pass
    /// <see cref="StorageTopologyRules.OnlyProviderPackagesMayReferenceAmazonS3"/>.
    /// </summary>
    [Fact]
    public void OnlyProviderPackagesMayReferenceAmazonS3_CleanConsumer_RulePasses()
    {
        const string abstractionsStubSource = """
            namespace SharedKernel.Storage
            {
                public interface IFileStorage { }
            }
            """;

        const string consumerSource = """
            namespace SomeConsumingService.Handlers
            {
                public class UploadDocumentHandler
                {
                    private readonly SharedKernel.Storage.IFileStorage _fileStorage;
                    public UploadDocumentHandler(SharedKernel.Storage.IFileStorage fileStorage)
                    {
                        _fileStorage = fileStorage;
                    }
                }
            }
            """;

        var abstractionsStubAssembly = CompileInMemory(
            "Fixture.ConsumerCleanTest.SharedKernel.Storage.Abstractions",
            abstractionsStubSource);
        var consumerAssembly = CompileInMemory(
            "CleanConsumer",
            consumerSource,
            extraReferences: new[] { abstractionsStubAssembly });

        var conditionList = StorageTopologyRules.OnlyProviderPackagesMayReferenceAmazonS3(consumerAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "UploadDocumentHandler depends only on IFileStorage, never a concrete Amazon.S3 type");
    }

    // ---------------------------------------------------------------------------
    // Real-assembly verification — the contrived fixtures above remain the primary red/green
    // proof; the real SharedKernel.Storage.Abstractions/.S3/.Obs assemblies are verified here too.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Real-assembly verification: the actual <c>SharedKernel.Storage.Abstractions</c> assembly has no
    /// AWS SDK, provider or Options-validation dependency — by namespace and by referenced assembly.
    /// </summary>
    [Fact]
    public void AbstractionsHasNoThirdPartyDependencies_RealAbstractionsAssembly_RulePasses()
    {
        var abstractionsAssembly = typeof(global::SharedKernel.Storage.IFileStorage).Assembly;

        var result = StorageTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly).GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real SharedKernel.Storage.Abstractions references only SharedKernel.Primitives and DI abstractions");
        StorageTopologyRules.AbstractionsForbiddenAssemblyReferences(abstractionsAssembly).Should().BeEmpty();
    }

    /// <summary>
    /// Real-assembly verification: the actual <c>SharedKernel.Storage.S3</c> assembly never references
    /// <c>SharedKernel.Storage.Obs</c>, by namespace or by assembly.
    /// </summary>
    [Fact]
    public void S3NeverReferencesObs_RealS3Assembly_BothHalvesPass()
    {
        var s3Assembly = typeof(global::SharedKernel.Storage.S3.S3StorageOptions).Assembly;

        StorageTopologyRules.S3NeverReferencesObs(s3Assembly).GetResult().IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Storage.S3 never names OBS");
        StorageTopologyRules.S3ForbiddenAssemblyReferences(s3Assembly).Should().BeEmpty(
            because: "SharedKernel.Storage.S3 must not reference SharedKernel.Storage.Obs");
    }

    /// <summary>
    /// Real-assembly verification of the intended direction: <c>SharedKernel.Storage.Obs</c> is a thin
    /// provider over <c>SharedKernel.Storage.S3</c>, so it references it.
    /// </summary>
    [Fact]
    public void RealObsAssembly_BuildsOnTheS3Provider()
    {
        var obsAssembly = typeof(global::SharedKernel.Storage.Obs.ObsStorageOptions).Assembly;

        obsAssembly.GetReferencedAssemblies().Select(a => a.Name).Should().Contain(
            "SharedKernel.Storage.S3",
            because: "OBS is served by the S3 implementation over its S3-compatible API");
    }

    /// <summary>
    /// Real-assembly verification: a non-provider real assembly (here,
    /// <c>SharedKernel.Storage.Abstractions</c> itself, which is neither <c>.S3</c> nor <c>.Obs</c>)
    /// has no dependency on <c>Amazon.S3</c>.
    /// </summary>
    [Fact]
    public void OnlyProviderPackagesMayReferenceAmazonS3_RealNonProviderAssembly_RulePasses()
    {
        var abstractionsAssembly = typeof(global::SharedKernel.Storage.IFileStorage).Assembly;

        var conditionList = StorageTopologyRules.OnlyProviderPackagesMayReferenceAmazonS3(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Storage.Abstractions has zero third-party dependencies, including Amazon.S3");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and loads it for reflection.
    /// </summary>
    /// <remarks>
    /// References supplied via <paramref name="extraReferences"/> are turned into
    /// <see cref="MetadataReference"/>s from their in-memory image
    /// (<see cref="MetadataReference.CreateFromImage(System.Collections.Immutable.ImmutableArray{byte})"/>)
    /// rather than from <see cref="Assembly.Location"/> — the same technique documented for
    /// <c>RedisTopologyRulesTests</c>, avoiding CS0234
    /// failures when chaining fixtures compiled in the same test run.
    /// </remarks>
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
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{assemblyName}_{System.Guid.NewGuid():N}.dll");

        using (var stream = new MemoryStream())
        {
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
            {
                var errors = string.Join(
                    System.Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                throw new InvalidOperationException(
                    $"Fixture '{assemblyName}' failed to compile:{System.Environment.NewLine}{errors}");
            }

            stream.Seek(0, SeekOrigin.Begin);
            File.WriteAllBytes(tempPath, stream.ToArray());
        }

        return Assembly.LoadFrom(tempPath);
    }

    /// <summary>
    /// Compiles <paramref name="source"/> to an assembly image without loading it — for a stub whose
    /// assembly name matches a real package (e.g. <c>SharedKernel.Storage.Obs</c>), which must never be
    /// loaded next to the real one; it is only ever used as a metadata reference.
    /// </summary>
    private static byte[] CompileImage(string assemblyName, string source, params byte[][] referenceImages)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };
        references.AddRange(referenceImages.Select(image =>
            MetadataReference.CreateFromImage(System.Collections.Immutable.ImmutableArray.Create(image))));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var errors = string.Join(
                System.Environment.NewLine,
                emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
            throw new InvalidOperationException($"Fixture '{assemblyName}' failed to compile:{System.Environment.NewLine}{errors}");
        }

        return stream.ToArray();
    }

    /// <summary>Writes <paramref name="image"/> to a temporary file and loads it for reflection.</summary>
    private static Assembly LoadImage(string assemblyName, byte[] image)
    {
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{assemblyName}_{System.Guid.NewGuid():N}.dll");
        File.WriteAllBytes(tempPath, image);
        return Assembly.LoadFrom(tempPath);
    }
}
