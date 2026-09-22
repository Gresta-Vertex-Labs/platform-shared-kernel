using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that mechanically enforce the <c>08.Storage</c> two-provider
/// package topology (<c>SharedKernel.Storage.Abstractions</c>, <c>SharedKernel.Storage.S3</c>,
/// <c>SharedKernel.Storage.Obs</c>) documented in prose by <c>08.Storage/CLAUDE.md</c>. Introduced
/// with the storage provider split.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="RedisTopologyRules"/>'s structure and its documented
/// <c>NotHaveDependencyOn</c> matching contract exactly (namespace <c>StartsWith</c>, no trailing
/// dot, self-collision awareness) but is scoped to two provider packages instead of Redis's five.
/// No Mono.Cecil, no <c>ICustomRule</c> — every check is a pure assembly-dependency-graph
/// predicate using <c>.Should().NotHaveDependencyOn(...)</c>, and no new SK diagnostic ID is
/// introduced by this class (SK0023 is a separate Roslyn analyzer covering the singleton-lifetime
/// concern; this class covers package topology only).
/// </para>
/// <para>
/// <strong>Matching note:</strong> NetArchTest's <c>NotHaveDependencyOn(term)</c> compares
/// <c>term</c> against each scanned type's set of dependency <em>namespaces</em> using a
/// <c>StartsWith</c> comparison, with no trailing dot on either side. <c>"Amazon"</c> is used as a
/// deliberate bare prefix — it catches every <c>AWSSDK.S3</c> namespace (root namespace
/// <c>Amazon</c>, covering <c>Amazon.S3</c>/<c>Amazon.Runtime</c>/etc. in one term) — while
/// <c>"SharedKernel.Storage.S3"</c>/<c>"SharedKernel.Storage.Obs"</c>/<c>"SharedKernel.Configuration"</c>
/// are exact package-identifying namespaces. None of the four terms is a prefix of
/// <c>"SharedKernel.Storage.Abstractions"</c> — no self-collision for
/// <see cref="AbstractionsHasNoThirdPartyDependencies"/>.
/// </para>
/// <para>
/// <strong>Permitted exemption list</strong> (caller-controlled — carries no internal namespace
/// guard, consistent with <c>PresentationLayeringRules</c>/<c>CompositionRootExclusivityRules</c>):
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>SharedKernel.Storage.S3</c> and <c>SharedKernel.Storage.Obs</c> — the only two
///     assemblies permitted to reference <c>Amazon.S3</c>; achieved by the caller never passing
///     either to <see cref="OnlyProviderPackagesMayReferenceAmazonS3"/>.
///   </description></item>
/// </list>
/// <para>
/// Any additional exemption must be documented in <c>00.Governance/CLAUDE.md</c> before it is
/// applied in code.
/// </para>
/// </remarks>
public static class StorageTopologyRules
{
    /// <summary>
    /// The four forbidden dependency terms for <c>SharedKernel.Storage.Abstractions</c>.
    /// </summary>
    private static readonly string[] AbstractionsForbiddenTerms =
    [
        "Amazon",
        "SharedKernel.Storage.S3",
        "SharedKernel.Storage.Obs",
        "SharedKernel.Configuration",
    ];

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that
    /// <c>SharedKernel.Storage.Abstractions</c> has no dependency on any of four forbidden terms:
    /// <c>"Amazon"</c> (bare prefix — catches every <c>AWSSDK.S3</c> namespace),
    /// <c>"SharedKernel.Storage.S3"</c>, <c>"SharedKernel.Storage.Obs"</c>, or
    /// <c>"SharedKernel.Configuration"</c> (the Options-validation package only the two provider
    /// packages need).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> <c>08.Storage/CLAUDE.md</c> documents
    /// <c>SharedKernel.Storage.Abstractions</c> as having "zero third-party NuGet dependencies —
    /// only a <c>SharedKernel.Primitives</c> project reference." This mechanically confirms the
    /// abstraction never accidentally couples to the AWS SDK, to either concrete provider package,
    /// or to the Options-validation package.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Storage.Abstractions</c> references
    /// <c>Amazon.S3.IAmazonS3</c> directly on an interface member.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> <c>SharedKernel.Storage.Abstractions</c> references only
    /// <c>SharedKernel.Primitives</c> (<c>Result</c>/<c>Result&lt;T&gt;</c>/<c>Error</c>) and BCL
    /// types.
    /// </para>
    /// </remarks>
    /// <param name="abstractionsAssembly">
    /// The <c>SharedKernel.Storage.Abstractions</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInAbstractions).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList AbstractionsHasNoThirdPartyDependencies(Assembly abstractionsAssembly)
    {
        ConditionList conditionList = Types
            .InAssembly(abstractionsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(AbstractionsForbiddenTerms[0]);

        for (var i = 1; i < AbstractionsForbiddenTerms.Length; i++)
        {
            conditionList = conditionList.And().NotHaveDependencyOn(AbstractionsForbiddenTerms[i]);
        }

        return conditionList;
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> array of exactly two elements asserting that
    /// <c>SharedKernel.Storage.S3</c> and <c>SharedKernel.Storage.Obs</c> never reference each
    /// other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO NAMED <see cref="Assembly"/> parameters (not <c>params Assembly[]</c>) — deliberate,
    /// mirroring <c>UnitOfWorkSeamRules</c>'s former <c>UnitOfWorkInterfacesRemainDistinct</c>'s
    /// two-named-parameter convention: the rule's whole purpose is comparing two specific, named
    /// packages, so positional <c>params</c> would obscure which assembly is expected to be which.
    /// Unlike <see cref="RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther"/> (which
    /// needs a lookup table to resolve each of four scanned assemblies' own identifying term
    /// before excluding it to avoid self-collision), only two packages exist here and neither
    /// identifying namespace (<c>"SharedKernel.Storage.S3"</c>, <c>"SharedKernel.Storage.Obs"</c>)
    /// is a prefix of the other or of its own declaring assembly — no lookup table needed.
    /// </para>
    /// <para>
    /// <strong>Rationale:</strong> the root <c>CLAUDE.md</c> documents S3 and Obs as sibling
    /// <c>.{Provider}</c> packages (not a <c>.{Provider}.Core</c>/<c>.{Provider}.{Role}</c> split)
    /// — <c>08.Storage/CLAUDE.md</c>'s own Provider role note states explicitly that they "must
    /// never reference each other," since a future native-OBS-SDK swap inside <c>.Obs</c> must
    /// never touch <c>.S3</c>'s implementation.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Storage.Obs</c> references a type from
    /// <c>SharedKernel.Storage.S3</c> (e.g., to reuse a constants class instead of independently
    /// declaring its own).
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> each provider package independently declares its own
    /// implementation shape (including its own <c>{Provider}StorageConstants</c>), referencing
    /// only <c>SharedKernel.Storage.Abstractions</c> and <c>SharedKernel.Configuration</c>.
    /// </para>
    /// </remarks>
    /// <param name="s3Assembly">
    /// The <c>SharedKernel.Storage.S3</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInS3).Assembly</c>.
    /// </param>
    /// <param name="obsAssembly">
    /// The <c>SharedKernel.Storage.Obs</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInObs).Assembly</c>.
    /// </param>
    /// <returns>
    /// A two-element array: index 0 asserts <c>SharedKernel.Storage.S3</c> has no dependency on
    /// <c>"SharedKernel.Storage.Obs"</c>; index 1 asserts <c>SharedKernel.Storage.Obs</c> has no
    /// dependency on <c>"SharedKernel.Storage.S3"</c>. The caller must assert
    /// <c>.GetResult().IsSuccessful</c> on EACH element.
    /// </returns>
    public static ConditionList[] ProviderPackagesNeverReferenceEachOther(
        Assembly s3Assembly,
        Assembly obsAssembly)
    {
        var s3NeverReferencesObs = Types
            .InAssembly(s3Assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.Storage.Obs");

        var obsNeverReferencesS3 = Types
            .InAssembly(obsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.Storage.S3");

        return [s3NeverReferencesObs, obsNeverReferencesS3];
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied
    /// <paramref name="assembliesUnderTest"/> has a dependency on <c>"Amazon.S3"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The structural sibling of
    /// <see cref="CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders"/>
    /// and <see cref="CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching"/>.
    /// Single <c>NotHaveDependencyOn("Amazon.S3")</c> call across every supplied assembly.
    /// </para>
    /// <para>
    /// <strong>Caller-controlled exclusion:</strong> the caller must NEVER include
    /// <c>SharedKernel.Storage.S3</c> or <c>SharedKernel.Storage.Obs</c> themselves in
    /// <paramref name="assembliesUnderTest"/> — exclusion is achieved entirely by caller choice of
    /// which assemblies to pass, the same caller-controlled exclusion convention as
    /// <c>PresentationLayeringRules</c>. There is no single internal namespace prefix that safely
    /// distinguishes "legitimate AWSSDK.S3 usage" from "leaked AWSSDK.S3 usage" other than which
    /// package the type lives in, which NetArchTest can only express by which assemblies are
    /// scanned, not by an internal exemption.
    /// </para>
    /// <para>
    /// <strong>Rationale:</strong> application code must inject
    /// <c>IFileStorage</c>/<c>IBlobUriGenerator</c> (<c>SharedKernel.Storage.Abstractions</c>) —
    /// never a concrete <c>Amazon.S3.IAmazonS3</c> type. A direct <c>Amazon.S3.*</c> reference
    /// anywhere outside the two provider packages defeats the abstraction split and makes a future
    /// provider swap (or a genuine Huawei-native-SDK migration inside <c>.Obs</c>) touch consumer
    /// code.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> a MediatR handler referencing
    /// <c>Amazon.S3.IAmazonS3</c> directly instead of <c>IFileStorage</c>.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> application code references only
    /// <c>SharedKernel.Storage.Abstractions</c>; <c>SharedKernel.Storage.S3</c>/<c>.Obs</c> are
    /// wired at the composition root via <c>AddSharedKernelS3Storage()</c>/
    /// <c>AddSharedKernelObsStorage()</c>.
    /// </para>
    /// </remarks>
    /// <param name="assembliesUnderTest">
    /// The production assemblies to check. Must NOT include <c>SharedKernel.Storage.S3</c> or
    /// <c>SharedKernel.Storage.Obs</c> — those are the two assemblies expected to legitimately
    /// reference <c>Amazon.S3</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList OnlyProviderPackagesMayReferenceAmazonS3(
        params Assembly[] assembliesUnderTest) =>
        Types
            .InAssemblies(assembliesUnderTest)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("Amazon.S3");
}
