using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that mechanically enforce the post-split five-package
/// Redis topology (<c>SharedKernel.Caching.Redis.Core</c>, <c>.Redis</c>,
/// <c>.Redis.DistributedLocking</c>, <c>.Redis.HashStore</c>, <c>.Redis.PubSub</c>) introduced
/// by the Redis role split, and re-affirm the <c>02.Caching</c> ↔ <c>07.Messaging</c> exclusion
/// boundary across the new package set.
/// </summary>
/// <remarks>
/// <para>
/// All five factory methods accept <see cref="Assembly"/> or <c>params Assembly[]</c> and return
/// <see cref="ConditionList"/> — consistent with the established <see cref="Helpers.ArchitectureRuleBase"/>
/// API. Every check is a pure assembly-dependency-graph predicate using
/// <c>.Should().NotHaveDependencyOn(...)</c>, in the same style as
/// <see cref="CachingAbstractionRules"/>. No Mono.Cecil, no <c>ICustomRule</c>, and no new SK
/// diagnostic IDs are introduced by this class.
/// </para>
/// <para>
/// <strong>Matching note:</strong> NetArchTest's <c>NotHaveDependencyOn(term)</c> compares
/// <c>term</c> against each scanned type's set of dependency <em>namespaces</em>
/// (the declaring namespace of every type referenced from that type's members) using a
/// <c>StartsWith</c> comparison, with <strong>no trailing dot</strong> on either side. Every
/// forbidden term in this class is therefore the exact namespace of the package it identifies —
/// e.g. <c>"SharedKernel.Caching.Redis.HashStore"</c> — never a bare root prefix such as
/// <c>"SharedKernel.Caching.Redis"</c>, which would also match <c>SharedKernel.Caching.Redis.Core</c>
/// and every other package whose namespace starts with that root. The L2 backplane package
/// (<c>SharedKernel.Caching.Redis</c>) has no single namespace of its own analogous to
/// <c>.Core</c>/<c>.DistributedLocking</c>/<c>.HashStore</c>/<c>.PubSub</c> — its types live under
/// <c>SharedKernel.Caching.Redis.Batch</c> and <c>SharedKernel.Caching.Redis.Extensions</c>, so both
/// are used as its identifying terms.
/// </para>
/// <para>
/// <strong>Self-dependency note:</strong> a type's dependency-namespace set includes its own
/// declaring namespace (types within the same namespace reference one another). Consequently a
/// package must never be checked against its own identifying term(s) — doing so produces a
/// guaranteed false positive against every type the package declares. See
/// <see cref="CapabilityPackagesNeverReferenceEachOther"/> for how this is handled when scanning
/// multiple capability assemblies.
/// </para>
/// <para>
/// <strong>Permitted cross-reference exemption list</strong> (must hold for
/// <see cref="RedisCoreNeverReferencesCapabilityPackages"/> and
/// <see cref="CapabilityPackagesNeverReferenceEachOther"/> to pass without modification):
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>Redis.Core</c> → <c>SharedKernel.Caching.Abstractions</c> (permitted; Core implements
///     abstraction-facing health/connection contracts).
///   </description></item>
///   <item><description>
///     <c>Redis</c> (L2), <c>Redis.DistributedLocking</c>, <c>Redis.HashStore</c>,
///     <c>Redis.PubSub</c> → <c>Redis.Core</c> (permitted; the shared foundation).
///   </description></item>
///   <item><description>
///     <c>Redis</c> (L2), <c>Redis.DistributedLocking</c>, <c>Redis.HashStore</c>,
///     <c>Redis.PubSub</c> → <c>SharedKernel.Caching.Abstractions</c> (permitted; each implements
///     abstraction interfaces).
///   </description></item>
/// </list>
/// <para>
/// Any additional exemption must be documented in <c>00.Governance/CLAUDE.md</c> before it is
/// applied in code.
/// </para>
/// </remarks>
public static class RedisTopologyRules
{
    /// <summary>
    /// The exact namespace terms that identify types declared in the <c>SharedKernel.Caching.Redis</c>
    /// (L2 backplane) package. The L2 package has no single dedicated sub-namespace of its own
    /// analogous to <c>.Core</c>/<c>.DistributedLocking</c>/<c>.HashStore</c>/<c>.PubSub</c> — its
    /// types live under <c>SharedKernel.Caching.Redis.Batch</c> and
    /// <c>SharedKernel.Caching.Redis.Extensions</c>. Both terms are required to fully identify an L2
    /// dependency; neither is a prefix of <c>SharedKernel.Caching.Redis.Core</c> or
    /// <c>SharedKernel.Caching.Redis.Core.Extensions</c>, so no collision with <c>Redis.Core</c>
    /// occurs.
    /// </summary>
    private static readonly string[] RedisL2NamespacePrefixes =
    [
        "SharedKernel.Caching.Redis.Batch",
        "SharedKernel.Caching.Redis.Extensions",
    ];

    /// <summary>
    /// The exact namespace terms that <c>SharedKernel.Caching.Redis.Core</c> must never depend on —
    /// the union of <see cref="RedisL2NamespacePrefixes"/> and the <c>.DistributedLocking</c>,
    /// <c>.HashStore</c>, and <c>.PubSub</c> capability namespaces. None of these terms is a prefix
    /// of <c>SharedKernel.Caching.Redis.Core</c> or <c>SharedKernel.Caching.Redis.Core.Extensions</c>,
    /// so checking <c>Redis.Core</c> against this set produces no self-collision.
    /// </summary>
    private static readonly string[] CapabilityPackageNamespacePrefixes =
    [
        .. RedisL2NamespacePrefixes,
        "SharedKernel.Caching.Redis.DistributedLocking",
        "SharedKernel.Caching.Redis.HashStore",
        "SharedKernel.Caching.Redis.PubSub",
    ];

    /// <summary>
    /// Maps each real capability package's assembly simple name to the exact namespace term(s) that
    /// identify types declared in that package. Used by
    /// <see cref="CapabilityPackagesNeverReferenceEachOther"/> to compute, per scanned assembly, the
    /// set of "other packages'" terms — excluding the scanned assembly's own terms — so that a
    /// package is never checked against its own declaring namespace.
    /// </summary>
    private static readonly Dictionary<string, string[]> CapabilityPackageOwnTermsByAssemblyName = new()
    {
        ["SharedKernel.Caching.Redis"] = RedisL2NamespacePrefixes,
        ["SharedKernel.Caching.Redis.DistributedLocking"] = ["SharedKernel.Caching.Redis.DistributedLocking"],
        ["SharedKernel.Caching.Redis.HashStore"] = ["SharedKernel.Caching.Redis.HashStore"],
        ["SharedKernel.Caching.Redis.PubSub"] = ["SharedKernel.Caching.Redis.PubSub"],
    };

    /// <summary>
    /// The four infrastructure-family terms that must never appear as a dependency of
    /// <c>SharedKernel.Caching.Abstractions</c>.
    /// </summary>
    private static readonly string[] AbstractionsForbiddenTerms =
    [
        "SharedKernel.Caching.Redis",
        "StackExchange.Redis",
        "Microsoft.EntityFrameworkCore",
        "MassTransit",
    ];

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that <c>SharedKernel.Caching.Redis.Core</c>
    /// has no dependency on any of the four capability packages: <c>SharedKernel.Caching.Redis</c>
    /// (L2), <c>SharedKernel.Caching.Redis.DistributedLocking</c>,
    /// <c>SharedKernel.Caching.Redis.HashStore</c>, or <c>SharedKernel.Caching.Redis.PubSub</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> <c>Redis.Core</c> is the shared connection/health/resilience
    /// foundation. A reference from Core to any capability package is a layering inversion —
    /// capability packages depend on Core, never the reverse.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Caching.Redis.Core</c> references a type
    /// from <c>SharedKernel.Caching.Redis.HashStore</c> (e.g., to reuse a hash-key helper).
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> shared helpers live in <c>Redis.Core</c> itself or in
    /// <c>SharedKernel.Caching.Abstractions</c>; capability packages reference <c>Redis.Core</c>,
    /// never the other direction.
    /// </para>
    /// </remarks>
    /// <param name="redisCoreAssembly">
    /// The <c>SharedKernel.Caching.Redis.Core</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInRedisCore).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList RedisCoreNeverReferencesCapabilityPackages(Assembly redisCoreAssembly)
    {
        var conditionList = Types
            .InAssembly(redisCoreAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(CapabilityPackageNamespacePrefixes[0]);

        for (var i = 1; i < CapabilityPackageNamespacePrefixes.Length; i++)
        {
            conditionList = conditionList.And().NotHaveDependencyOn(CapabilityPackageNamespacePrefixes[i]);
        }

        return conditionList;
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that none of the four Redis capability
    /// packages (<c>SharedKernel.Caching.Redis</c> (L2), <c>SharedKernel.Caching.Redis.DistributedLocking</c>,
    /// <c>SharedKernel.Caching.Redis.HashStore</c>, <c>SharedKernel.Caching.Redis.PubSub</c>)
    /// references any of the other three.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> sibling role-packages must depend only on
    /// <c>.{Provider}.Core</c> (root <c>CLAUDE.md</c> "Provider role-split variant" rule) — a
    /// sibling-to-sibling reference (e.g., <c>Redis.DistributedLocking</c> →
    /// <c>Redis.HashStore</c>) is exactly the shortcut this rule forecloses.
    /// </para>
    /// <para>
    /// <c>SharedKernel.Caching.Redis.Core</c> and <c>SharedKernel.Caching.Abstractions</c> are
    /// excluded from the forbidden set for every capability package — both are permitted
    /// dependencies (see the exemption list on <see cref="RedisTopologyRules"/>).
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Caching.Redis.DistributedLocking</c>
    /// references a type from <c>SharedKernel.Caching.Redis.HashStore</c>.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> each capability package references only
    /// <c>SharedKernel.Caching.Redis.Core</c> and <c>SharedKernel.Caching.Abstractions</c>.
    /// </para>
    /// </remarks>
    /// <param name="capabilityAssemblies">
    /// The capability package assemblies under test — supply any subset of
    /// <c>SharedKernel.Caching.Redis</c> (L2), <c>SharedKernel.Caching.Redis.DistributedLocking</c>,
    /// <c>SharedKernel.Caching.Redis.HashStore</c>, and <c>SharedKernel.Caching.Redis.PubSub</c> via
    /// <c>typeof(SomeTypeInPackage).Assembly</c>. Do not pass <c>Redis.Core</c> or
    /// <c>Caching.Abstractions</c> — they are the permitted shared dependencies, not capability
    /// packages under this check.
    /// </param>
    /// <returns>
    /// One <see cref="ConditionList"/> per element of <paramref name="capabilityAssemblies"/>, in the
    /// same order. Each <see cref="ConditionList"/> asserts that no type in that assembly depends on
    /// any of the <em>other</em> capability packages' identifying namespace terms — the scanned
    /// assembly's own terms (resolved via
    /// <see cref="CapabilityPackageOwnTermsByAssemblyName"/> by assembly simple name) are excluded
    /// from its own checked set, eliminating the self-dependency false positive described in the
    /// class-level <strong>Self-dependency note</strong>. An assembly whose simple name is not a
    /// recognized capability package (e.g., a test fixture) is checked against the full term set,
    /// since it owns none of the four capability namespaces. Assert each element via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList[] CapabilityPackagesNeverReferenceEachOther(
        params Assembly[] capabilityAssemblies)
    {
        var results = new ConditionList[capabilityAssemblies.Length];

        for (var assemblyIndex = 0; assemblyIndex < capabilityAssemblies.Length; assemblyIndex++)
        {
            var assembly = capabilityAssemblies[assemblyIndex];
            var assemblyName = assembly.GetName().Name ?? string.Empty;

            var otherTerms = CapabilityPackageOwnTermsByAssemblyName
                .Where(entry => entry.Key != assemblyName)
                .SelectMany(entry => entry.Value)
                .ToArray();

            ConditionList conditionList = Types
                .InAssembly(assembly)
                .That()
                .HaveNameStartingWith(string.Empty)
                .Should()
                .NotHaveDependencyOn(otherTerms[0]);

            for (var termIndex = 1; termIndex < otherTerms.Length; termIndex++)
            {
                conditionList = conditionList.And().NotHaveDependencyOn(otherTerms[termIndex]);
            }

            results[assemblyIndex] = conditionList;
        }

        return results;
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that
    /// <c>SharedKernel.Caching.Redis.PubSub</c> has no dependency on any assembly whose name
    /// starts with <c>"SharedKernel.Messaging"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> codifies the Issue 3 boundary — <c>Redis.PubSub</c> is an
    /// ephemeral, no-delivery-guarantee signaling channel (<c>ICacheInvalidationBus</c> /
    /// <c>IRedisChannelService</c>) and must never become a backdoor path into the durable
    /// <c>IMessageBus</c> abstraction.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Caching.Redis.PubSub</c> references a
    /// type from <c>SharedKernel.Messaging.Abstractions</c> or <c>SharedKernel.Messaging.MassTransit</c>.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> <c>Redis.PubSub</c> depends only on
    /// <c>SharedKernel.Caching.Redis.Core</c> and <c>SharedKernel.Caching.Abstractions</c>; durable
    /// delivery flows through <c>07.Messaging</c> independently.
    /// </para>
    /// </remarks>
    /// <param name="pubSubAssembly">
    /// The <c>SharedKernel.Caching.Redis.PubSub</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInPubSub).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList PubSubNeverReferencesMessaging(Assembly pubSubAssembly) =>
        Types
            .InAssembly(pubSubAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.Messaging");

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no assembly in
    /// <c>SharedKernel.Messaging.*</c> (the caller supplies
    /// <c>SharedKernel.Messaging.Abstractions</c> and <c>SharedKernel.Messaging.MassTransit</c>)
    /// has a dependency on any assembly whose name starts with <c>"SharedKernel.Caching"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> this is the structural converse of
    /// <see cref="PubSubNeverReferencesMessaging"/> and of the root <c>CLAUDE.md</c> hard rule
    /// ("<c>07.Messaging</c> must never reference any <c>SharedKernel.Caching.*</c> package, and no
    /// <c>SharedKernel.Caching.*</c> package may reference any <c>SharedKernel.Messaging.*</c>
    /// package"). Both directions must be independently asserted because NetArchTest dependency
    /// checks are directional — passing <see cref="PubSubNeverReferencesMessaging"/> does not imply
    /// this rule passes.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Messaging.MassTransit</c> references a
    /// type from <c>SharedKernel.Caching.Redis.PubSub</c> (e.g., to publish a cache-invalidation
    /// signal alongside a domain event).
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> <c>SharedKernel.Messaging.*</c> packages depend only on
    /// layers 01–04 per the root layering table; cache invalidation signaling is composed
    /// independently at the service composition root.
    /// </para>
    /// </remarks>
    /// <param name="messagingAssemblies">
    /// The <c>SharedKernel.Messaging.*</c> assemblies under test — typically
    /// <c>SharedKernel.Messaging.Abstractions</c> and <c>SharedKernel.Messaging.MassTransit</c>,
    /// supplied via <c>typeof(SomeTypeInPackage).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting that no supplied messaging assembly depends on any
    /// assembly whose name starts with <c>"SharedKernel.Caching"</c>.
    /// </returns>
    public static ConditionList MessagingNeverReferencesCaching(params Assembly[] messagingAssemblies) =>
        Types
            .InAssemblies(messagingAssemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.Caching");

    /// <summary>
    /// Returns a <see cref="ConditionList"/> re-verifying that
    /// <c>SharedKernel.Caching.Abstractions</c> has zero dependencies beyond
    /// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>, across the now five-package
    /// Redis topology.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> confirms that the five-package split did not introduce a
    /// transitive dependency from any new capability package back into the abstraction the
    /// capability packages themselves implement. Same shape as
    /// <see cref="SharedKernelLayeringRules.CoreReferencesNothing"/>, scoped to
    /// <c>SharedKernel.Caching.Abstractions</c>.
    /// </para>
    /// <para>
    /// Asserts <c>.Should().NotHaveDependencyOn(term)</c> for each of:
    /// <c>"SharedKernel.Caching.Redis"</c> (covers the L2 package and all four
    /// <c>Redis.Core</c>/<c>.DistributedLocking</c>/<c>.HashStore</c>/<c>.PubSub</c> capability
    /// packages by prefix), <c>"StackExchange.Redis"</c>, <c>"Microsoft.EntityFrameworkCore"</c>,
    /// and <c>"MassTransit"</c>.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Caching.Abstractions</c> references
    /// <c>StackExchange.Redis</c> directly (e.g., to expose a Redis-specific type on an interface).
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> <c>SharedKernel.Caching.Abstractions</c> references only
    /// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> (and
    /// <c>Microsoft.Extensions.Options</c>); all Redis-specific types live in the capability
    /// packages that implement the abstraction interfaces.
    /// </para>
    /// </remarks>
    /// <param name="abstractionsAssembly">
    /// The <c>SharedKernel.Caching.Abstractions</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInAbstractions).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList CachingAbstractionsHasNoInfrastructureDependencies(
        Assembly abstractionsAssembly)
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
}
