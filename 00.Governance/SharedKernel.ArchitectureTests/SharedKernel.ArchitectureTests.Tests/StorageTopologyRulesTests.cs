using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="StorageTopologyRules"/> — the <c>08.Storage</c> two-provider package
/// topology enforcement predicates introduced by WO-043 P-271.
/// </summary>
/// <remarks>
/// <para>
/// T-194/T-195 cover <see cref="StorageTopologyRules.AbstractionsHasNoThirdPartyDependencies"/>.
/// T-196/T-197 cover <see cref="StorageTopologyRules.ProviderPackagesNeverReferenceEachOther"/>.
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
            namespace SharedKernel.Storage.Abstractions.Abstractions
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
            namespace SharedKernel.Storage.Abstractions.Abstractions
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
    // T-196 — Fire path: S3-shaped fixture references Obs-shaped fixture, and vice versa
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-196: When a contrived "SharedKernel.Storage.S3"-shaped assembly references a type whose
    /// declaring assembly simulates <c>SharedKernel.Storage.Obs</c>,
    /// <see cref="StorageTopologyRules.ProviderPackagesNeverReferenceEachOther"/> must fail on
    /// array element [0] only. The converse (Obs referencing S3) must fail on array element [1]
    /// only.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_S3ReferencesObs_FirstElementFails()
    {
        const string obsStubSource = """
            namespace SharedKernel.Storage.Obs
            {
                public interface IObsFileStorage { }
            }
            """;

        const string s3Source = """
            namespace SharedKernel.Storage.S3
            {
                public class LeakyS3FileStorage
                {
                    private readonly SharedKernel.Storage.Obs.IObsFileStorage _obsFileStorage;
                    public LeakyS3FileStorage(SharedKernel.Storage.Obs.IObsFileStorage obsFileStorage)
                    {
                        _obsFileStorage = obsFileStorage;
                    }
                }
            }
            """;

        const string cleanObsSource = """
            namespace SharedKernel.Storage.Obs
            {
                public class CleanObsFileStorage { }
            }
            """;

        var obsStubAssembly = CompileInMemory("Fixture.ProviderSiblingTest.SharedKernel.Storage.Obs", obsStubSource);
        var violatingS3Assembly = CompileInMemory(
            "ViolatingSharedKernel.Storage.S3",
            s3Source,
            extraReferences: new[] { obsStubAssembly });
        var cleanObsAssembly = CompileInMemory("CleanSharedKernel.Storage.Obs", cleanObsSource);

        var conditionLists = StorageTopologyRules.ProviderPackagesNeverReferenceEachOther(
            violatingS3Assembly,
            cleanObsAssembly);

        conditionLists.Should().HaveCount(2);

        conditionLists[0].GetResult().IsSuccessful.Should().BeFalse(
            because: "LeakyS3FileStorage references SharedKernel.Storage.Obs directly");
        conditionLists[1].GetResult().IsSuccessful.Should().BeTrue(
            because: "the clean Obs fixture has no dependency on SharedKernel.Storage.S3");
    }

    /// <summary>
    /// T-196 (converse): When a contrived "SharedKernel.Storage.Obs"-shaped assembly references a
    /// type whose declaring assembly simulates <c>SharedKernel.Storage.S3</c>,
    /// <see cref="StorageTopologyRules.ProviderPackagesNeverReferenceEachOther"/> must fail on
    /// array element [1] only.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_ObsReferencesS3_SecondElementFails()
    {
        const string s3StubSource = """
            namespace SharedKernel.Storage.S3
            {
                public interface IS3FileStorage { }
            }
            """;

        const string obsSource = """
            namespace SharedKernel.Storage.Obs
            {
                public class LeakyObsFileStorage
                {
                    private readonly SharedKernel.Storage.S3.IS3FileStorage _s3FileStorage;
                    public LeakyObsFileStorage(SharedKernel.Storage.S3.IS3FileStorage s3FileStorage)
                    {
                        _s3FileStorage = s3FileStorage;
                    }
                }
            }
            """;

        const string cleanS3Source = """
            namespace SharedKernel.Storage.S3
            {
                public class CleanS3FileStorage { }
            }
            """;

        var s3StubAssembly = CompileInMemory("Fixture.ProviderSiblingConverseTest.SharedKernel.Storage.S3", s3StubSource);
        var violatingObsAssembly = CompileInMemory(
            "ViolatingSharedKernel.Storage.Obs",
            obsSource,
            extraReferences: new[] { s3StubAssembly });
        var cleanS3Assembly = CompileInMemory("CleanSharedKernel.Storage.S3", cleanS3Source);

        var conditionLists = StorageTopologyRules.ProviderPackagesNeverReferenceEachOther(
            cleanS3Assembly,
            violatingObsAssembly);

        conditionLists.Should().HaveCount(2);

        conditionLists[0].GetResult().IsSuccessful.Should().BeTrue(
            because: "the clean S3 fixture has no dependency on SharedKernel.Storage.Obs");
        conditionLists[1].GetResult().IsSuccessful.Should().BeFalse(
            because: "LeakyObsFileStorage references SharedKernel.Storage.S3 directly");
    }

    // ---------------------------------------------------------------------------
    // T-197 — Pass path: S3/Obs fixtures with no cross-reference pass both array elements
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-197: Contrived "SharedKernel.Storage.S3"- and "SharedKernel.Storage.Obs"-shaped
    /// assemblies with no cross-reference must pass both array elements of
    /// <see cref="StorageTopologyRules.ProviderPackagesNeverReferenceEachOther"/>.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_NoCrossReference_BothElementsPass()
    {
        const string s3Source = """
            namespace SharedKernel.Storage.S3
            {
                public class CleanS3FileStorage { }
            }
            """;

        const string obsSource = """
            namespace SharedKernel.Storage.Obs
            {
                public class CleanObsFileStorage { }
            }
            """;

        var s3Assembly = CompileInMemory("CleanSharedKernel.Storage.S3.NoCross", s3Source);
        var obsAssembly = CompileInMemory("CleanSharedKernel.Storage.Obs.NoCross", obsSource);

        var conditionLists = StorageTopologyRules.ProviderPackagesNeverReferenceEachOther(s3Assembly, obsAssembly);

        conditionLists.Should().HaveCount(2);
        foreach (var conditionList in conditionLists)
        {
            conditionList.GetResult().IsSuccessful.Should().BeTrue(
                because: "neither provider package references the other");
        }
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
            namespace SharedKernel.Storage.Abstractions.Abstractions
            {
                public interface IFileStorage { }
            }
            """;

        const string consumerSource = """
            namespace SomeConsumingService.Handlers
            {
                public class UploadDocumentHandler
                {
                    private readonly SharedKernel.Storage.Abstractions.Abstractions.IFileStorage _fileStorage;
                    public UploadDocumentHandler(SharedKernel.Storage.Abstractions.Abstractions.IFileStorage fileStorage)
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
    // Real-assembly verification — 08.Storage reached Published (P-265/P-266/P-267 all shipped)
    // before this phase's implementation session; the contrived fixtures above remain the
    // primary red/green proof per the phase spec, but the real assemblies are also verified here
    // since the dependency this phase originally flagged as blocking has resolved.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Real-assembly verification: the actual <c>SharedKernel.Storage.Abstractions</c> assembly
    /// (P-265) has zero third-party dependencies — confirms the contrived-fixture proof (T-194/
    /// T-195) generalizes to the shipped package.
    /// </summary>
    [Fact]
    public void AbstractionsHasNoThirdPartyDependencies_RealAbstractionsAssembly_RulePasses()
    {
        var abstractionsAssembly = typeof(Storage.Abstractions.Abstractions.IFileStorage).Assembly;

        var conditionList = StorageTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real SharedKernel.Storage.Abstractions references only SharedKernel.Primitives");
    }

    /// <summary>
    /// Real-assembly verification: the actual <c>SharedKernel.Storage.S3</c> (P-266) and
    /// <c>SharedKernel.Storage.Obs</c> (P-267) assemblies never reference each other.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_RealS3AndObsAssemblies_BothElementsPass()
    {
        var s3Assembly = typeof(Storage.S3.Options.S3StorageOptions).Assembly;
        var obsAssembly = typeof(Storage.Obs.Options.ObsStorageOptions).Assembly;

        var conditionLists = StorageTopologyRules.ProviderPackagesNeverReferenceEachOther(s3Assembly, obsAssembly);

        conditionLists.Should().HaveCount(2);
        foreach (var conditionList in conditionLists)
        {
            conditionList.GetResult().IsSuccessful.Should().BeTrue(
                because: "SharedKernel.Storage.S3 and SharedKernel.Storage.Obs are independently-declared siblings");
        }
    }

    /// <summary>
    /// Real-assembly verification: a non-provider real assembly (here,
    /// <c>SharedKernel.Storage.Abstractions</c> itself, which is neither <c>.S3</c> nor <c>.Obs</c>)
    /// has no dependency on <c>Amazon.S3</c>.
    /// </summary>
    [Fact]
    public void OnlyProviderPackagesMayReferenceAmazonS3_RealNonProviderAssembly_RulePasses()
    {
        var abstractionsAssembly = typeof(Storage.Abstractions.Abstractions.IFileStorage).Assembly;

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
    /// <c>RedisTopologyRulesTests</c>/<c>CompositionRootExclusivityRulesTests</c>, avoiding CS0234
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
}
