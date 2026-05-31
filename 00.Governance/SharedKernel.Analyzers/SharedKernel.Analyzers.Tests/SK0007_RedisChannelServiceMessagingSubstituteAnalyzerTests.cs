using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0007 <see cref="RedisChannelServiceMessagingSubstituteAnalyzer"/>.</summary>
public class SK0007_RedisChannelServiceMessagingSubstituteAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-20 — Fire path: IRedisChannelService in a command/event handler class
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-20: Injecting <c>IRedisChannelService</c> via constructor in a class named
    /// <c>PlaceOrderCommandHandler</c> in namespace <c>Application.Commands</c> triggers SK0007.
    /// </summary>
    [Fact]
    public async Task FirePath_IRedisChannelServiceInCommandHandler_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RedisChannelServiceMessagingSubstituteAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IRedisChannelService { }

                namespace Application.Commands
                {
                    public class PlaceOrderCommandHandler
                    {
                        public PlaceOrderCommandHandler({|SK0007:IRedisChannelService|} redisChannel) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: <c>IRedisChannelService</c> as a field in an event-context class triggers SK0007.
    /// </summary>
    [Fact]
    public async Task FirePath_IRedisChannelServiceAsFieldInEventClass_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RedisChannelServiceMessagingSubstituteAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IRedisChannelService { }

                namespace Messaging
                {
                    public class OrderCreatedEventPublisher
                    {
                        private readonly {|SK0007:IRedisChannelService|} _redis;
                        public OrderCreatedEventPublisher(object dummy) { _redis = null!; }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-21 — Pass path: IRedisChannelService in a non-messaging class
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-21: <c>IRedisChannelService</c> injected in a class named <c>CacheInvalidationService</c>
    /// with no forbidden name or namespace term — no diagnostic.
    /// </summary>
    [Fact]
    public async Task PassPath_IRedisChannelServiceInCacheService_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RedisChannelServiceMessagingSubstituteAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IRedisChannelService { }

                namespace Infrastructure.Cache
                {
                    public class CacheInvalidationService
                    {
                        public CacheInvalidationService(IRedisChannelService redis) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-22 — Suppression path: inside SharedKernel.Caching namespace
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-22: <c>IRedisChannelService</c> injected inside <c>SharedKernel.Caching.Redis</c>
    /// namespace — no diagnostic (suppressed).
    /// </summary>
    [Fact]
    public async Task SuppressionPath_InsideCachingNamespace_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RedisChannelServiceMessagingSubstituteAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IRedisChannelService { }

                namespace SharedKernel.Caching.Redis
                {
                    // This is the service's own definition — no diagnostic expected
                    public class RedisCacheInvalidationBroadcaster
                    {
                        private readonly IRedisChannelService _redis;
                        public RedisCacheInvalidationBroadcaster(IRedisChannelService redis) { _redis = redis; }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Suppression path: inside <c>SharedKernel.Caching</c> namespace (not the Redis sub-namespace).
    /// </summary>
    [Fact]
    public async Task SuppressionPath_InsideCachingRootNamespace_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RedisChannelServiceMessagingSubstituteAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IRedisChannelService { }

                namespace SharedKernel.Caching
                {
                    public class CacheEventPublisher
                    {
                        private readonly IRedisChannelService _redis;
                        public CacheEventPublisher(IRedisChannelService redis) { _redis = redis; }
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}
