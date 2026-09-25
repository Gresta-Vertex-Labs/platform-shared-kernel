using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates and assembly-reference checks that mechanically enforce the
/// <c>08.Storage</c> package topology (<c>SharedKernel.Storage.Abstractions</c>,
/// <c>SharedKernel.Storage.S3</c>, <c>SharedKernel.Storage.Obs</c>) documented in prose by
/// <c>08.Storage/CLAUDE.md</c>. Introduced with the storage provider split.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Topology:</strong> <c>SharedKernel.Storage.Obs</c> is a thin provider over
/// <c>SharedKernel.Storage.S3</c> — OBS is served through its S3-compatible API, so <c>.Obs</c>
/// references <c>.S3</c> and adds only OBS configuration and a compatibility profile. The direction is
/// one-way: <c>.S3</c> must never reference <c>.Obs</c>, and the abstractions reference neither.
/// This deliberately reverses the original sibling rule ("S3 and Obs never reference each other"),
/// which forced the OBS package to duplicate the whole S3 implementation.
/// </para>
/// <para>
/// Mirrors <see cref="RedisTopologyRules"/>'s structure and its documented
/// <c>NotHaveDependencyOn</c> matching contract (namespace <c>StartsWith</c>, no trailing dot,
/// self-collision awareness). No Mono.Cecil, no <c>ICustomRule</c>, and no new SK diagnostic ID
/// (SK0023 is a separate Roslyn analyzer covering the <c>IAmazonS3</c> singleton-lifetime concern;
/// this class covers package topology only).
/// </para>
/// <para>
/// <strong>Why namespace checks are paired with assembly-reference checks:</strong> the storage
/// packages put their registration entry points in the shared <c>SharedKernel.Storage</c> namespace
/// (<c>AddSharedKernelStorage()</c>, <c>AddS3</c>, <c>AddObs</c>, <c>S3StorageBuilder</c>), so a
/// dependency on, say, <c>AddObs</c> is invisible to a <c>NotHaveDependencyOn("SharedKernel.Storage.Obs")</c>
/// namespace check. <see cref="AbstractionsForbiddenAssemblyReferences"/> and
/// <see cref="S3ForbiddenAssemblyReferences"/> close that gap by inspecting the referenced assembly
/// names; assert both halves of each rule.
/// </para>
/// <para>
/// <strong>Matching note:</strong> NetArchTest's <c>NotHaveDependencyOn(term)</c> compares
/// <c>term</c> against each scanned type's set of dependency <em>namespaces</em> using a
/// <c>StartsWith</c> comparison, with no trailing dot on either side. <c>"Amazon"</c> is used as a
/// deliberate bare prefix — it catches every <c>AWSSDK.S3</c> namespace (root namespace
/// <c>Amazon</c>, covering <c>Amazon.S3</c>/<c>Amazon.Runtime</c>/etc. in one term) — while
/// <c>"SharedKernel.Storage.S3"</c>/<c>"SharedKernel.Storage.Obs"</c>/<c>"SharedKernel.Configuration"</c>
/// are exact package-identifying namespaces. None of the four terms is a prefix of the abstractions'
/// own <c>SharedKernel.Storage</c> namespace — no self-collision for
/// <see cref="AbstractionsHasNoThirdPartyDependencies"/>.
/// </para>
/// <para>
/// <strong>Permitted exemption list</strong> (caller-controlled — carries no internal namespace
/// guard, consistent with <c>PresentationLayeringRules</c>):
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
    /// <summary>The Obs provider's assembly and namespace, which <c>SharedKernel.Storage.S3</c> must never reference.</summary>
    private const string ObsPackage = "SharedKernel.Storage.Obs";

    /// <summary>
    /// The four forbidden dependency namespaces for <c>SharedKernel.Storage.Abstractions</c>.
    /// </summary>
    private static readonly string[] AbstractionsForbiddenTerms =
    [
        "Amazon",
        "SharedKernel.Storage.S3",
        ObsPackage,
        "SharedKernel.Configuration",
    ];

    /// <summary>
    /// The forbidden referenced-assembly name prefixes for <c>SharedKernel.Storage.Abstractions</c>:
    /// the AWS SDK (<c>AWSSDK.*</c>), both provider packages and the Options-validation package.
    /// </summary>
    private static readonly string[] AbstractionsForbiddenAssemblyPrefixes =
    [
        "AWSSDK",
        "SharedKernel.Storage.S3",
        ObsPackage,
        "SharedKernel.Configuration",
    ];

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that
    /// <c>SharedKernel.Storage.Abstractions</c> has no dependency on any of four forbidden terms:
    /// <c>"Amazon"</c> (bare prefix — catches every <c>AWSSDK.S3</c> namespace),
    /// <c>"SharedKernel.Storage.S3"</c>, <c>"SharedKernel.Storage.Obs"</c>, or
    /// <c>"SharedKernel.Configuration"</c> (the Options-validation package only the provider
    /// packages need).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> <c>08.Storage/CLAUDE.md</c> documents
    /// <c>SharedKernel.Storage.Abstractions</c> as having no cloud SDK dependency — only a
    /// <c>SharedKernel.Primitives</c> project reference plus
    /// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> for the store registry. This
    /// mechanically confirms the abstraction never accidentally couples to the AWS SDK, to either
    /// concrete provider package, or to the Options-validation package. Pair with
    /// <see cref="AbstractionsForbiddenAssemblyReferences"/>.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Storage.Abstractions</c> references
    /// <c>Amazon.S3.IAmazonS3</c> directly on an interface member.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> <c>SharedKernel.Storage.Abstractions</c> references only
    /// <c>SharedKernel.Primitives</c> (<c>Result</c>/<c>Result&lt;T&gt;</c>/<c>Error</c>), DI
    /// abstractions and BCL types.
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
    /// Returns the names of every assembly <paramref name="abstractionsAssembly"/> references that it
    /// must not: <c>AWSSDK.*</c>, <c>SharedKernel.Storage.S3</c>, <c>SharedKernel.Storage.Obs</c> or
    /// <c>SharedKernel.Configuration</c>. Empty when compliant.
    /// </summary>
    /// <remarks>
    /// The assembly-level half of <see cref="AbstractionsHasNoThirdPartyDependencies"/>: it also
    /// catches a dependency on a provider type declared in the shared <c>SharedKernel.Storage</c>
    /// namespace, which no namespace check can distinguish from the abstractions' own types.
    /// </remarks>
    /// <param name="abstractionsAssembly">The <c>SharedKernel.Storage.Abstractions</c> assembly under test.</param>
    /// <returns>The offending referenced assembly names.</returns>
    public static IReadOnlyList<string> AbstractionsForbiddenAssemblyReferences(Assembly abstractionsAssembly)
    {
        ArgumentNullException.ThrowIfNull(abstractionsAssembly);

        return abstractionsAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => AbstractionsForbiddenAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in <c>SharedKernel.Storage.S3</c>
    /// depends on the <c>SharedKernel.Storage.Obs</c> namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> <c>SharedKernel.Storage.Obs</c> is a thin provider built on
    /// <c>SharedKernel.Storage.S3</c> (see the class remarks). A reference back from <c>.S3</c> to
    /// <c>.Obs</c> would be a cycle in intent — the generic S3 implementation knowing about one
    /// S3-compatible vendor — and every S3-only service would then restore the OBS package. Vendor
    /// differences belong in <c>.Obs</c>'s compatibility profile, passed down to <c>.S3</c>
    /// through <c>AddS3Compatible</c>. Pair with <see cref="S3ForbiddenAssemblyReferences"/>.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Storage.S3</c> reads
    /// <c>ObsStorageOptions</c> to special-case OBS inside the S3 store.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> <c>SharedKernel.Storage.Obs</c> calls
    /// <c>AddS3Compatible(...)</c> with an OBS client and an <c>S3Compatibility</c> profile;
    /// <c>SharedKernel.Storage.S3</c> never names OBS.
    /// </para>
    /// </remarks>
    /// <param name="s3Assembly">
    /// The <c>SharedKernel.Storage.S3</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInS3).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList S3NeverReferencesObs(Assembly s3Assembly) =>
        Types
            .InAssembly(s3Assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(ObsPackage);

    /// <summary>
    /// Returns the names of every assembly <paramref name="s3Assembly"/> references that it must not —
    /// <c>SharedKernel.Storage.Obs</c>. Empty when compliant.
    /// </summary>
    /// <remarks>
    /// The assembly-level half of <see cref="S3NeverReferencesObs"/>: <c>AddObs</c> lives in the
    /// shared <c>SharedKernel.Storage</c> namespace, so only the referenced assembly name reveals a
    /// dependency on it.
    /// </remarks>
    /// <param name="s3Assembly">The <c>SharedKernel.Storage.S3</c> assembly under test.</param>
    /// <returns>The offending referenced assembly names.</returns>
    public static IReadOnlyList<string> S3ForbiddenAssemblyReferences(Assembly s3Assembly)
    {
        ArgumentNullException.ThrowIfNull(s3Assembly);

        return s3Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith(ObsPackage, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied
    /// <paramref name="assembliesUnderTest"/> has a dependency on <c>"Amazon.S3"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Complements the tier check, which cannot see a direct AWS SDK reference from an Adapter or Host package.
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
    /// <strong>Rationale:</strong> application code must inject the named stores
    /// (<c>IFileStorage</c>, <c>ITenantFileStorage</c>, <c>IFileStorageFactory</c> from
    /// <c>SharedKernel.Storage.Abstractions</c>) — never a concrete <c>Amazon.S3.IAmazonS3</c> type. A
    /// direct <c>Amazon.S3.*</c> reference anywhere outside the two provider packages defeats the
    /// abstraction split and makes a provider swap touch consumer code.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> a MediatR handler referencing
    /// <c>Amazon.S3.IAmazonS3</c> directly instead of <c>IFileStorage</c>.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> application code references only
    /// <c>SharedKernel.Storage.Abstractions</c>; the providers are wired at the composition root via
    /// <c>AddSharedKernelStorage().AddS3(configuration)</c> or <c>.AddObs(configuration)</c> followed by
    /// <c>.AddStore(name)</c>.
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
