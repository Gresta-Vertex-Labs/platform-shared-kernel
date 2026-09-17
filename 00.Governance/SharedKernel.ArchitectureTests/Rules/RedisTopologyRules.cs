using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

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
/// Every factory method accepts <see cref="Assembly"/> or <c>params Assembly[]</c> and returns
/// <see cref="ConditionList"/> (or one per assembly) — consistent with the established
/// <see cref="Helpers.ArchitectureRuleBase"/> API. The dependency checks use
/// <c>.Should().NotHaveDependencyOn(...)</c>, in the same style as
/// <see cref="CachingAbstractionRules"/>; <see cref="CachingAbstractionsDeclaresNoProviderSpecificTypes"/>
/// matches type names, and <see cref="CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions"/>
/// reads the compiled assembly references through
/// <see cref="Predicates.AssemblyReferenceAllowListPredicate"/>. No SK diagnostic IDs are
/// introduced by this class.
/// </para>
/// <para>
/// <strong>Provider-neutral contract.</strong> <c>SharedKernel.Caching.Abstractions</c> holds only
/// provider-neutral contracts (cache, tenant cache, key format, distributed locks and leases).
/// Provider-specific contracts live in the package that implements them:
/// <c>IRedisChannelService</c> in <c>SharedKernel.Caching.Redis.PubSub</c>,
/// <c>IRedisHashService</c>/<c>ITypedHashStore&lt;T&gt;</c> in
/// <c>SharedKernel.Caching.Redis.HashStore</c>, and <c>IRedisConnectionProbe</c> in
/// <c>SharedKernel.Caching.Redis.Core.Health</c>.
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
///     <c>Redis.Core</c> → <c>SharedKernel.Caching.Abstractions</c> (permitted; the shared
///     connection layer may use the provider-neutral contracts).
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
    /// The namespace terms that must never appear as a dependency of
    /// <c>SharedKernel.Caching.Abstractions</c>: every caching provider package and provider
    /// library (FusionCache, Redis, RedLock.net, Polly), the options and hosting stacks the
    /// provider-neutral contract deliberately does without, and the persistence/messaging families.
    /// </summary>
    private static readonly string[] AbstractionsForbiddenTerms =
    [
        "SharedKernel.Caching.Redis",
        "SharedKernel.Caching.FusionCache",
        "StackExchange.Redis",
        "ZiggyCreatures",
        "RedLockNet",
        "Polly",
        "Microsoft.Extensions.Caching",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Hosting",
        "Microsoft.EntityFrameworkCore",
        "MassTransit",
    ];

    /// <summary>
    /// The only non-BCL assembly <c>SharedKernel.Caching.Abstractions</c> may reference —
    /// <c>ICachingBuilder</c> exposes <c>IServiceCollection</c> so provider packages can chain
    /// registrations.
    /// </summary>
    private const string DependencyInjectionAbstractionsAssemblyName =
        "Microsoft.Extensions.DependencyInjection.Abstractions";

    /// <summary>
    /// Matches a type name that names a caching provider or a provider-side concept. A
    /// provider-neutral contract has no Redis channel, hash store, FusionCache option, RedLock
    /// handle, or connection health check — those belong to the provider packages
    /// (<c>IRedisChannelService</c> in <c>.Redis.PubSub</c>, <c>IRedisHashService</c>/
    /// <c>ITypedHashStore&lt;T&gt;</c> in <c>.Redis.HashStore</c>, <c>IRedisConnectionProbe</c> in
    /// <c>.Redis.Core.Health</c>).
    /// </summary>
    private const string ProviderSpecificTypeNamePattern =
        "Redis|Fusion|RedLock|StackExchange|Garnet|Valkey|Memcache|Connection";

    /// <summary>
    /// The namespace root of RedLock.net (<c>RedLockNet.SERedis</c>, <c>RedLockNet.Abstractions</c>),
    /// which <c>SharedKernel.Caching.Redis.DistributedLocking</c> replaced with its own atomic Lua
    /// scripts.
    /// </summary>
    private const string RedLockNetNamespaceRoot = "RedLockNet";

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that <c>SharedKernel.Caching.Redis.Core</c>
    /// has no dependency on any of the four capability packages: <c>SharedKernel.Caching.Redis</c>
    /// (L2), <c>SharedKernel.Caching.Redis.DistributedLocking</c>,
    /// <c>SharedKernel.Caching.Redis.HashStore</c>, or <c>SharedKernel.Caching.Redis.PubSub</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> <c>Redis.Core</c> is the shared connection and health-probe
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
    /// ephemeral, no-delivery-guarantee signaling channel (<c>IRedisChannelService</c>) and must
    /// never become a backdoor path into the durable
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
    /// Returns a <see cref="ConditionList"/> asserting that no type in
    /// <c>SharedKernel.Caching.Abstractions</c> depends on a caching provider, a provider library,
    /// the options or hosting stacks, or the persistence/messaging families.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> the abstractions package is the provider-neutral contract every
    /// provider implements. A dependency on any provider — or on a library only a provider needs —
    /// drags that provider into every consumer and lets provider concepts leak into the contract.
    /// Same shape as <see cref="SharedKernelLayeringRules.CoreReferencesNothing"/>, scoped to
    /// <c>SharedKernel.Caching.Abstractions</c>.
    /// </para>
    /// <para>
    /// Asserts <c>.Should().NotHaveDependencyOn(term)</c> for each of:
    /// <c>"SharedKernel.Caching.Redis"</c> (covers the L2 package and all four
    /// <c>Redis.Core</c>/<c>.DistributedLocking</c>/<c>.HashStore</c>/<c>.PubSub</c> capability
    /// packages by prefix), <c>"SharedKernel.Caching.FusionCache"</c>, <c>"StackExchange.Redis"</c>,
    /// <c>"ZiggyCreatures"</c> (FusionCache), <c>"RedLockNet"</c>, <c>"Polly"</c>,
    /// <c>"Microsoft.Extensions.Caching"</c>, <c>"Microsoft.Extensions.Options"</c>,
    /// <c>"Microsoft.Extensions.Hosting"</c>, <c>"Microsoft.EntityFrameworkCore"</c>, and
    /// <c>"MassTransit"</c>.
    /// </para>
    /// <para>
    /// Namespace terms cannot separate two assemblies that share a namespace; pair this rule with
    /// <see cref="CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions"/>, which pins the
    /// exact assembly set.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Caching.Abstractions</c> references
    /// <c>StackExchange.Redis</c> directly (e.g., to expose a Redis-specific type on an interface),
    /// or binds a configuration type through <c>IOptions&lt;T&gt;</c>.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> <c>SharedKernel.Caching.Abstractions</c> references only
    /// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>; options, connection state and
    /// every Redis-specific type live in the provider packages that implement the contract.
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

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that the compiled
    /// <c>SharedKernel.Caching.Abstractions</c> assembly references no assembly other than the .NET
    /// base class library and <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> a deny-list of namespaces only catches the dependencies someone
    /// thought to list. This allow-list reads the assembly's actual references, so any new package
    /// dependency — a provider, <c>Microsoft.Extensions.Options</c>, a logging or hosting package,
    /// another SharedKernel package — fails until the contract is deliberately widened here.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> an options type bound with <c>IOptions&lt;T&gt;</c>, a
    /// <c>BackgroundService</c>, or a reference to <c>SharedKernel.Primitives</c> added to the
    /// abstractions package.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> the package references
    /// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> for <c>ICachingBuilder</c> and
    /// nothing else; configuration and hosting live in the provider packages.
    /// </para>
    /// <para>
    /// An assembly with no types passes vacuously; callers should pass the real compiled assembly.
    /// </para>
    /// </remarks>
    /// <param name="abstractionsAssembly">
    /// The <c>SharedKernel.Caching.Abstractions</c> assembly under test — supply via
    /// <c>typeof(ICacheService).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>. When it fails, every type of the assembly is
    /// reported, because the violation belongs to the assembly rather than to one type.
    /// </returns>
    public static ConditionList CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions(
        Assembly abstractionsAssembly) =>
        Types
            .InAssembly(abstractionsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new AssemblyReferenceAllowListPredicate(DependencyInjectionAbstractionsAssemblyName));

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that <c>SharedKernel.Caching.Abstractions</c>
    /// declares no provider-specific type: no type whose name mentions Redis, FusionCache,
    /// RedLock, StackExchange, Garnet, Valkey, Memcache, or a connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> the dependency rules above stop the abstractions package from
    /// <em>using</em> a provider, but a provider-shaped contract can be written with no provider
    /// dependency at all — <c>IRedisChannelService</c>, <c>IRedisHashService</c>,
    /// <c>ITypedHashStore&lt;T&gt;</c> and a <c>ConnectionHealthState</c> enum once lived here that way.
    /// Those contracts now belong to the package that implements them —
    /// <c>SharedKernel.Caching.Redis.PubSub</c> and <c>SharedKernel.Caching.Redis.HashStore</c> — and
    /// connection health is now <c>IRedisConnectionProbe</c> in <c>SharedKernel.Caching.Redis.Core.Health</c>.
    /// </para>
    /// <para>
    /// Matching is a case-sensitive regular expression over the simple type name, so compiler-
    /// generated and nested types are checked too.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>public interface IRedisStreamService</c> or
    /// <c>public enum ConnectionHealthState</c> declared in <c>SharedKernel.Caching.Abstractions</c>.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> the provider package declares its own contract in its
    /// own namespace, e.g. <c>SharedKernel.Caching.Redis.PubSub.IRedisChannelService</c>.
    /// </para>
    /// </remarks>
    /// <param name="abstractionsAssembly">
    /// The <c>SharedKernel.Caching.Abstractions</c> assembly under test — supply via
    /// <c>typeof(ICacheService).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList CachingAbstractionsDeclaresNoProviderSpecificTypes(Assembly abstractionsAssembly) =>
        Types
            .InAssembly(abstractionsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveNameMatching(ProviderSpecificTypeNamePattern);

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that
    /// <c>SharedKernel.Caching.Redis.DistributedLocking</c> has no dependency on RedLock.net
    /// (<c>RedLockNet.*</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> the lock service issues its fencing token atomically with the
    /// acquisition, keeps a held lock alive and reports its loss, all inside server-side Lua
    /// scripts. RedLock.net acquires through its own multi-step protocol, which cannot issue a
    /// token in the same atomic step, so reintroducing it would reopen the gap between "lock
    /// acquired" and "token issued" that a stale holder can exploit.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>RedisDistributedLockService</c> constructed over
    /// <c>RedLockNet.IDistributedLockFactory</c>.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> acquisition, extension and release run as Lua scripts
    /// over <c>StackExchange.Redis</c>'s <c>IDatabase</c>.
    /// </para>
    /// </remarks>
    /// <param name="distributedLockingAssembly">
    /// The <c>SharedKernel.Caching.Redis.DistributedLocking</c> assembly under test — supply via
    /// <c>typeof(RedisDistributedLockingExtensions).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList DistributedLockingNeverReferencesRedLock(Assembly distributedLockingAssembly) =>
        Types
            .InAssembly(distributedLockingAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(RedLockNetNamespaceRoot);
}
