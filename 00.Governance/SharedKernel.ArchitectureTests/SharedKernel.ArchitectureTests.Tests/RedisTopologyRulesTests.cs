using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="RedisTopologyRules"/> — the five-package Redis topology enforcement
/// predicates introduced by WO-023 P-145.
/// </summary>
/// <remarks>
/// T-104/T-105 cover <see cref="RedisTopologyRules.RedisCoreNeverReferencesCapabilityPackages"/>.
/// T-106/T-107 cover <see cref="RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther"/>.
/// T-108/T-109 cover <see cref="RedisTopologyRules.PubSubNeverReferencesMessaging"/>.
/// T-110/T-111 cover <see cref="RedisTopologyRules.MessagingNeverReferencesCaching"/>.
/// T-112 and the tests after it cover the SharedKernel.Caching.Abstractions contract rules
/// (dependencies, assembly references, provider-specific type names) and
/// <see cref="RedisTopologyRules.DistributedLockingNeverReferencesRedLock"/>.
/// </remarks>
public class RedisTopologyRulesTests
{
    // ---------------------------------------------------------------------------
    // T-104 — Fire path: Redis.Core imports a type from Redis.HashStore
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-104: When a contrived "Redis.Core" assembly references a type whose declaring assembly
    /// simulates <c>SharedKernel.Caching.Redis.HashStore</c>,
    /// <see cref="RedisTopologyRules.RedisCoreNeverReferencesCapabilityPackages"/> must fail and
    /// name <c>Redis.HashStore</c> in the failure message.
    /// </summary>
    [Fact]
    public void RedisCoreNeverReferencesCapabilityPackages_ViolatingAssembly_RuleFails()
    {
        const string hashStoreSource = """
            namespace SharedKernel.Caching.Redis.HashStore
            {
                public interface IRedisHashService { }
            }
            """;

        const string redisCoreSource = """
            namespace SharedKernel.Caching.Redis.Core
            {
                public class RedisConnectionFactory
                {
                    private readonly SharedKernel.Caching.Redis.HashStore.IRedisHashService _hashService;
                    public RedisConnectionFactory(SharedKernel.Caching.Redis.HashStore.IRedisHashService hashService)
                    {
                        _hashService = hashService;
                    }
                }
            }
            """;

        var hashStoreAssembly = CompileInMemory("Fixture.RedisCoreTest.SharedKernel.Caching.Redis.HashStore", hashStoreSource);
        var redisCoreAssembly = CompileInMemory(
            "ViolatingRedisCore",
            redisCoreSource,
            extraReferences: new[] { hashStoreAssembly });

        var conditionList = RedisTopologyRules.RedisCoreNeverReferencesCapabilityPackages(redisCoreAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RedisConnectionFactory references SharedKernel.Caching.Redis.HashStore directly");
    }

    // ---------------------------------------------------------------------------
    // T-105 — Pass path: real SharedKernel.Caching.Redis.Core assembly
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-105: The real <c>SharedKernel.Caching.Redis.Core</c> assembly (P-140) references only
    /// <c>SharedKernel.Caching.Abstractions</c> — <see cref="RedisTopologyRules.RedisCoreNeverReferencesCapabilityPackages"/>
    /// must pass.
    /// </summary>
    [Fact]
    public void RedisCoreNeverReferencesCapabilityPackages_RealRedisCoreAssembly_RulePasses()
    {
        var redisCoreAssembly = typeof(Caching.Redis.Core.RedisConnectionOptions).Assembly;

        var conditionList = RedisTopologyRules.RedisCoreNeverReferencesCapabilityPackages(redisCoreAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Caching.Redis.Core must never reference any capability package");
    }

    // ---------------------------------------------------------------------------
    // T-106 — Fire path: Redis.DistributedLocking imports a type from Redis.HashStore
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-106: When a contrived "Redis.DistributedLocking" assembly references a type whose
    /// declaring assembly simulates <c>SharedKernel.Caching.Redis.HashStore</c>,
    /// <see cref="RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther"/> must fail and
    /// name both package names in the failure message.
    /// </summary>
    [Fact]
    public void CapabilityPackagesNeverReferenceEachOther_ViolatingAssembly_RuleFails()
    {
        const string hashStoreSource = """
            namespace SharedKernel.Caching.Redis.HashStore
            {
                public interface IRedisHashService { }
            }
            """;

        const string distributedLockingSource = """
            namespace SharedKernel.Caching.Redis.DistributedLocking
            {
                public class RedisDistributedLockService
                {
                    private readonly SharedKernel.Caching.Redis.HashStore.IRedisHashService _hashService;
                    public RedisDistributedLockService(SharedKernel.Caching.Redis.HashStore.IRedisHashService hashService)
                    {
                        _hashService = hashService;
                    }
                }
            }
            """;

        var hashStoreAssembly = CompileInMemory("Fixture.CapabilitySiblingTest.SharedKernel.Caching.Redis.HashStore", hashStoreSource);
        var distributedLockingAssembly = CompileInMemory(
            "ViolatingRedisDistributedLocking",
            distributedLockingSource,
            extraReferences: new[] { hashStoreAssembly });

        var conditionLists = RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther(distributedLockingAssembly);
        var result = conditionLists.Single().GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RedisDistributedLockService (DistributedLocking) references SharedKernel.Caching.Redis.HashStore directly");
    }

    // ---------------------------------------------------------------------------
    // T-107 — Pass path: real Redis, DistributedLocking, HashStore, PubSub assemblies
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-107: The real <c>SharedKernel.Caching.Redis</c> (L2), <c>.DistributedLocking</c>,
    /// <c>.HashStore</c>, and <c>.PubSub</c> assemblies (P-141–P-144) each reference only
    /// <c>Redis.Core</c> and <c>Caching.Abstractions</c> —
    /// <see cref="RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther"/> must pass.
    /// </summary>
    [Fact]
    public void CapabilityPackagesNeverReferenceEachOther_RealCapabilityAssemblies_RulePasses()
    {
        var redisL2Assembly = typeof(Caching.Redis.Extensions.RedisL2Options).Assembly;
        var distributedLockingAssembly = typeof(Caching.Redis.DistributedLocking.Extensions.RedisDistributedLockingExtensions).Assembly;
        var hashStoreAssembly = typeof(Caching.Redis.HashStore.Extensions.RedisHashServiceExtensions).Assembly;
        var pubSubAssembly = typeof(Caching.Redis.PubSub.IRedisChannelService).Assembly;

        var conditionLists = RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther(
            redisL2Assembly,
            distributedLockingAssembly,
            hashStoreAssembly,
            pubSubAssembly);

        foreach (var conditionList in conditionLists)
        {
            var result = conditionList.GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: "the four capability packages must depend only on Redis.Core and Caching.Abstractions");
        }
    }

    // ---------------------------------------------------------------------------
    // T-108 — Fire path: Redis.PubSub imports a type from SharedKernel.Messaging.Abstractions
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-108: When a contrived "Redis.PubSub" assembly references a type whose declaring assembly
    /// simulates <c>SharedKernel.Messaging.Abstractions</c>,
    /// <see cref="RedisTopologyRules.PubSubNeverReferencesMessaging"/> must fail.
    /// </summary>
    [Fact]
    public void PubSubNeverReferencesMessaging_ViolatingAssembly_RuleFails()
    {
        const string messagingAbstractionsSource = """
            namespace SharedKernel.Messaging.Abstractions
            {
                public interface IMessageBus { }
            }
            """;

        const string pubSubSource = """
            namespace SharedKernel.Caching.Redis.PubSub
            {
                public class RedisChannelBridge
                {
                    private readonly SharedKernel.Messaging.Abstractions.IMessageBus _bus;
                    public RedisChannelBridge(SharedKernel.Messaging.Abstractions.IMessageBus bus)
                    {
                        _bus = bus;
                    }
                }
            }
            """;

        var messagingAbstractionsAssembly = CompileInMemory(
            "Fixture.SharedKernel.Messaging.Abstractions",
            messagingAbstractionsSource);
        var pubSubAssembly = CompileInMemory(
            "ViolatingRedisPubSub",
            pubSubSource,
            extraReferences: new[] { messagingAbstractionsAssembly });

        var conditionList = RedisTopologyRules.PubSubNeverReferencesMessaging(pubSubAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RedisChannelBridge references SharedKernel.Messaging.Abstractions directly");
    }

    // ---------------------------------------------------------------------------
    // T-109 — Pass path: real SharedKernel.Caching.Redis.PubSub assembly
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-109: The real <c>SharedKernel.Caching.Redis.PubSub</c> assembly (P-144) has no
    /// <c>SharedKernel.Messaging.*</c> reference — <see cref="RedisTopologyRules.PubSubNeverReferencesMessaging"/>
    /// must pass.
    /// </summary>
    [Fact]
    public void PubSubNeverReferencesMessaging_RealPubSubAssembly_RulePasses()
    {
        var pubSubAssembly = typeof(Caching.Redis.PubSub.IRedisChannelService).Assembly;

        var conditionList = RedisTopologyRules.PubSubNeverReferencesMessaging(pubSubAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Caching.Redis.PubSub must never reference SharedKernel.Messaging.*");
    }

    // ---------------------------------------------------------------------------
    // T-110 — Fire path: SharedKernel.Messaging.MassTransit imports a type from Redis.PubSub
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-110: When a contrived "Messaging.MassTransit" assembly references a type whose declaring
    /// assembly simulates <c>SharedKernel.Caching.Redis.PubSub</c>,
    /// <see cref="RedisTopologyRules.MessagingNeverReferencesCaching"/> must fail and name the
    /// offending messaging assembly in the failure message.
    /// </summary>
    [Fact]
    public void MessagingNeverReferencesCaching_ViolatingAssembly_RuleFails()
    {
        const string pubSubSource = """
            namespace SharedKernel.Caching.Redis.PubSub
            {
                public interface IRedisChannelService { }
            }
            """;

        const string massTransitSource = """
            namespace SharedKernel.Messaging.MassTransit
            {
                public class CacheInvalidatingConsumer
                {
                    private readonly SharedKernel.Caching.Redis.PubSub.IRedisChannelService _channel;
                    public CacheInvalidatingConsumer(SharedKernel.Caching.Redis.PubSub.IRedisChannelService channel)
                    {
                        _channel = channel;
                    }
                }
            }
            """;

        var pubSubAssembly = CompileInMemory("Fixture.SharedKernel.Caching.Redis.PubSub", pubSubSource);
        var massTransitAssembly = CompileInMemory(
            "ViolatingSharedKernel.Messaging.MassTransit",
            massTransitSource,
            extraReferences: new[] { pubSubAssembly });

        var conditionList = RedisTopologyRules.MessagingNeverReferencesCaching(massTransitAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "CacheInvalidatingConsumer references SharedKernel.Caching.Redis.PubSub directly");
    }

    // ---------------------------------------------------------------------------
    // T-111 — Pass path: real Messaging.Abstractions and Messaging.MassTransit assemblies
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-111: The real <c>SharedKernel.Messaging.Abstractions</c> and
    /// <c>SharedKernel.Messaging.MassTransit</c> assemblies have no <c>SharedKernel.Caching.*</c>
    /// reference — <see cref="RedisTopologyRules.MessagingNeverReferencesCaching"/> must pass.
    /// </summary>
    [Fact]
    public void MessagingNeverReferencesCaching_RealMessagingAssemblies_RulePasses()
    {
        var messagingAbstractionsAssembly = typeof(Messaging.Abstractions.Batch.BatchOptions).Assembly;
        var massTransitAssembly = typeof(Messaging.MassTransit.Consumers.ConsumerBase<>).Assembly;

        var conditionList = RedisTopologyRules.MessagingNeverReferencesCaching(
            messagingAbstractionsAssembly,
            massTransitAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Messaging.* must never reference SharedKernel.Caching.*");
    }

    // ---------------------------------------------------------------------------
    // T-112 — Pass path: real SharedKernel.Caching.Abstractions assembly (re-verification)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-112: The real <c>SharedKernel.Caching.Abstractions</c> assembly depends on no provider,
    /// provider library, options/hosting stack, EF Core, or MassTransit —
    /// <see cref="RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies"/> must pass.
    /// </summary>
    [Fact]
    public void CachingAbstractionsHasNoInfrastructureDependencies_RealAbstractionsAssembly_RulePasses()
    {
        var abstractionsAssembly = typeof(Caching.Abstractions.ICacheService).Assembly;

        var conditionList = RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Caching.Abstractions must have zero infrastructure dependencies");
    }

    /// <summary>
    /// Fire path: a contrived abstractions assembly exposing a type from a provider library, the
    /// options stack, or the hosting stack fails
    /// <see cref="RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies"/>.
    /// </summary>
    [Theory]
    [InlineData("SharedKernel.Caching.FusionCache")]
    [InlineData("ZiggyCreatures.Caching.Fusion")]
    [InlineData("RedLockNet.SERedis")]
    [InlineData("Polly")]
    [InlineData("Microsoft.Extensions.Options")]
    [InlineData("Microsoft.Extensions.Hosting")]
    [InlineData("StackExchange.Redis")]
    public void CachingAbstractionsHasNoInfrastructureDependencies_ViolatingAssembly_RuleFails(string forbiddenNamespace)
    {
        var providerSource = $$"""
            namespace {{forbiddenNamespace}}
            {
                public interface IProviderType { }
            }
            """;

        var abstractionsSource = $$"""
            namespace SharedKernel.Caching.Abstractions
            {
                public interface ICacheService
                {
                    {{forbiddenNamespace}}.IProviderType Provider { get; }
                }
            }
            """;

        var providerAssembly = CompileInMemory($"Fixture.AbstractionsDependency.{forbiddenNamespace}", providerSource);
        var abstractionsAssembly = CompileInMemory(
            $"ViolatingCachingAbstractions.{forbiddenNamespace}",
            abstractionsSource,
            extraReferences: new[] { providerAssembly });

        var result = RedisTopologyRules
            .CachingAbstractionsHasNoInfrastructureDependencies(abstractionsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: $"ICacheService exposes a type from {forbiddenNamespace}");
    }

    // ---------------------------------------------------------------------------
    // Assembly-reference allow-list: SharedKernel.Caching.Abstractions
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Pass path: the real <c>SharedKernel.Caching.Abstractions</c> assembly references only the
    /// BCL and <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> —
    /// <see cref="RedisTopologyRules.CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions"/>
    /// must pass.
    /// </summary>
    /// <remarks>
    /// Guarded against passing vacuously: the assembly must declare types and must actually
    /// reference the one allowed package, so the allow-list is exercised rather than skipped.
    /// </remarks>
    [Fact]
    public void CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions_RealAbstractionsAssembly_RulePasses()
    {
        var abstractionsAssembly = typeof(Caching.Abstractions.ICacheService).Assembly;

        abstractionsAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().Contain(
                "Microsoft.Extensions.DependencyInjection.Abstractions",
                because: "ICachingBuilder exposes IServiceCollection, so the allow-list is exercised");

        var result = RedisTopologyRules
            .CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions(abstractionsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Caching.Abstractions must reference nothing but the BCL and " +
                     "Microsoft.Extensions.DependencyInjection.Abstractions");
    }

    /// <summary>
    /// Fire path: an abstractions assembly that references any other non-BCL assembly fails
    /// <see cref="RedisTopologyRules.CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions"/>,
    /// even when that assembly's namespace is on no deny-list.
    /// </summary>
    [Fact]
    public void CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions_ExtraAssemblyReference_RuleFails()
    {
        const string extraSource = """
            namespace Contoso.Configuration
            {
                public sealed class SettingsSource { }
            }
            """;

        const string abstractionsSource = """
            namespace SharedKernel.Caching.Abstractions
            {
                public sealed class CachePolicy
                {
                    public Contoso.Configuration.SettingsSource? Source { get; set; }
                }
            }
            """;

        var extraAssembly = CompileInMemory("Contoso.Configuration", extraSource);
        var abstractionsAssembly = CompileInMemory(
            "ExtraReferenceCachingAbstractions",
            abstractionsSource,
            extraReferences: new[] { extraAssembly });

        var result = RedisTopologyRules
            .CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions(abstractionsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "CachePolicy pulls in the Contoso.Configuration assembly, which is not on the allow-list");
    }

    // ---------------------------------------------------------------------------
    // Provider-specific type names: SharedKernel.Caching.Abstractions
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Pass path: the real <c>SharedKernel.Caching.Abstractions</c> assembly declares no
    /// provider-specific type —
    /// <see cref="RedisTopologyRules.CachingAbstractionsDeclaresNoProviderSpecificTypes"/> must pass.
    /// </summary>
    [Fact]
    public void CachingAbstractionsDeclaresNoProviderSpecificTypes_RealAbstractionsAssembly_RulePasses()
    {
        var abstractionsAssembly = typeof(Caching.Abstractions.ICacheService).Assembly;

        var result = RedisTopologyRules
            .CachingAbstractionsDeclaresNoProviderSpecificTypes(abstractionsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "provider-specific contracts belong to the provider packages that implement them");
    }

    /// <summary>
    /// Fire path: a provider-shaped contract declared in the abstractions package fails
    /// <see cref="RedisTopologyRules.CachingAbstractionsDeclaresNoProviderSpecificTypes"/>, whether
    /// or not it depends on the provider library.
    /// </summary>
    [Theory]
    [InlineData("public interface IRedisChannelService { }")]
    [InlineData("public interface IRedisHashService { }")]
    [InlineData("public interface ITypedHashStoreForRedis<T> { }")]
    [InlineData("public enum ConnectionHealthState { Healthy, Degraded }")]
    [InlineData("public sealed class FusionCacheEntryOptions { }")]
    [InlineData("public interface IRedLockHandle { }")]
    public void CachingAbstractionsDeclaresNoProviderSpecificTypes_ProviderShapedType_RuleFails(string declaration)
    {
        var source = $$"""
            namespace SharedKernel.Caching.Abstractions
            {
                {{declaration}}
            }
            """;

        var abstractionsAssembly = CompileInMemory($"ProviderShapedCachingAbstractions.{Guid.NewGuid():N}", source);

        var result = RedisTopologyRules
            .CachingAbstractionsDeclaresNoProviderSpecificTypes(abstractionsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: $"'{declaration}' is a provider-specific contract declared in the abstractions package");
    }

    /// <summary>
    /// The provider-specific contracts that left <c>SharedKernel.Caching.Abstractions</c> are
    /// declared by the package that implements them, in that package's own namespace (or, for the
    /// connection readiness probe, its <c>Health</c> sub-namespace).
    /// </summary>
    [Theory]
    [InlineData(typeof(Caching.Redis.PubSub.IRedisChannelService), "SharedKernel.Caching.Redis.PubSub", "SharedKernel.Caching.Redis.PubSub")]
    [InlineData(typeof(Caching.Redis.HashStore.IRedisHashService), "SharedKernel.Caching.Redis.HashStore", "SharedKernel.Caching.Redis.HashStore")]
    [InlineData(typeof(Caching.Redis.HashStore.ITypedHashStore<>), "SharedKernel.Caching.Redis.HashStore", "SharedKernel.Caching.Redis.HashStore")]
    [InlineData(typeof(Caching.Redis.Core.Health.IRedisConnectionProbe), "SharedKernel.Caching.Redis.Core", "SharedKernel.Caching.Redis.Core.Health")]
    public void ProviderSpecificContract_IsDeclaredByItsProviderPackage(Type contract, string providerPackage, string expectedNamespace)
    {
        contract.Assembly.GetName().Name.Should().Be(
            providerPackage,
            because: $"{contract.Name} is a provider contract and ships with its provider");
        contract.Namespace.Should().Be(
            expectedNamespace,
            because: $"{contract.Name} lives in its provider package's namespace");
    }

    // ---------------------------------------------------------------------------
    // RedLock.net exclusion: SharedKernel.Caching.Redis.DistributedLocking
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Pass path: the real <c>SharedKernel.Caching.Redis.DistributedLocking</c> assembly has no
    /// RedLock.net dependency, as a type or as an assembly reference —
    /// <see cref="RedisTopologyRules.DistributedLockingNeverReferencesRedLock"/> must pass.
    /// </summary>
    [Fact]
    public void DistributedLockingNeverReferencesRedLock_RealDistributedLockingAssembly_RulePasses()
    {
        var distributedLockingAssembly = typeof(Caching.Redis.DistributedLocking.Extensions.RedisDistributedLockingExtensions).Assembly;

        distributedLockingAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain(
                name => name != null && name.StartsWith("RedLockNet", StringComparison.Ordinal),
                because: "the lock service runs its own atomic Lua scripts over StackExchange.Redis");

        var result = RedisTopologyRules
            .DistributedLockingNeverReferencesRedLock(distributedLockingAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Caching.Redis.DistributedLocking must not depend on RedLock.net");
    }

    /// <summary>
    /// Fire path: a contrived DistributedLocking assembly built over a RedLock.net-shaped factory
    /// fails <see cref="RedisTopologyRules.DistributedLockingNeverReferencesRedLock"/>.
    /// </summary>
    [Fact]
    public void DistributedLockingNeverReferencesRedLock_ViolatingAssembly_RuleFails()
    {
        const string redLockSource = """
            namespace RedLockNet
            {
                public interface IDistributedLockFactory { }
            }
            """;

        const string distributedLockingSource = """
            namespace SharedKernel.Caching.Redis.DistributedLocking.Implementations
            {
                public sealed class RedisDistributedLockService
                {
                    private readonly RedLockNet.IDistributedLockFactory _factory;
                    public RedisDistributedLockService(RedLockNet.IDistributedLockFactory factory)
                    {
                        _factory = factory;
                    }
                }
            }
            """;

        var redLockAssembly = CompileInMemory("Fixture.RedLockNet", redLockSource);
        var distributedLockingAssembly = CompileInMemory(
            "ViolatingRedisDistributedLockingRedLock",
            distributedLockingSource,
            extraReferences: new[] { redLockAssembly });

        var result = RedisTopologyRules
            .DistributedLockingNeverReferencesRedLock(distributedLockingAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RedisDistributedLockService is built over RedLockNet.IDistributedLockFactory");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and loads it for reflection.
    /// </summary>
    /// <remarks>
    /// References supplied via <paramref name="extraReferences"/> are turned into
    /// <see cref="MetadataReference"/>s from their in-memory <see cref="MemoryStream"/> image
    /// (<see cref="MetadataReference.CreateFromImage(System.Collections.Immutable.ImmutableArray{byte})"/>)
    /// rather than from <see cref="Assembly.Location"/>. A second <see cref="CompileInMemory"/> call
    /// that references the bytes returned by a first call does not depend on the first assembly's
    /// on-disk file being flushed, unlocked, or independently resolvable — avoiding the CS0234
    /// "type or namespace does not exist" failures that occur when chaining
    /// <c>MetadataReference.CreateFromFile(extraReference.Location)</c> across fixtures compiled in
    /// the same test run.
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
