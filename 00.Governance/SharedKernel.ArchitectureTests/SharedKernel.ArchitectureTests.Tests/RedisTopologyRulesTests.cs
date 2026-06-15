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
/// T-112 covers <see cref="RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies"/>.
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
                public class RedLockProvider
                {
                    private readonly SharedKernel.Caching.Redis.HashStore.IRedisHashService _hashService;
                    public RedLockProvider(SharedKernel.Caching.Redis.HashStore.IRedisHashService hashService)
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
            because: "RedLockProvider (DistributedLocking) references SharedKernel.Caching.Redis.HashStore directly");
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
        var distributedLockingAssembly = typeof(Caching.Redis.DistributedLocking.Extensions.RedisLockOptions).Assembly;
        var hashStoreAssembly = typeof(Caching.Redis.HashStore.Extensions.RedisHashServiceExtensions).Assembly;
        var pubSubAssembly = typeof(Caching.Redis.PubSub.CacheInvalidationReceiver).Assembly;

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
        var pubSubAssembly = typeof(Caching.Redis.PubSub.CacheInvalidationReceiver).Assembly;

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
    /// T-112: The real <c>SharedKernel.Caching.Abstractions</c> assembly references only
    /// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> (and
    /// <c>Microsoft.Extensions.Options</c>) —
    /// <see cref="RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies"/> must
    /// pass against <c>Redis.Core</c>, <c>StackExchange.Redis</c>, EF Core, and MassTransit terms.
    /// </summary>
    [Fact]
    public void CachingAbstractionsHasNoInfrastructureDependencies_RealAbstractionsAssembly_RulePasses()
    {
        var abstractionsAssembly = typeof(Caching.Abstractions.ICacheInvalidationBus).Assembly;

        var conditionList = RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Caching.Abstractions must have zero infrastructure dependencies");
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
