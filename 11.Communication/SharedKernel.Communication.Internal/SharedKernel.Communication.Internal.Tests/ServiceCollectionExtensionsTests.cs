using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Internal.Extensions;
using SharedKernel.Communication.Internal.Options;
using SharedKernel.Communication.Internal.Resolvers;

namespace SharedKernel.Communication.Internal.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    // ─────────────────────────────────────────────────────────────
    // AddStaticServiceDiscovery — guard tests (T-07)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddStaticServiceDiscovery_ThrowsInvalidOperationException_WhenResolverAlreadyRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServiceEndpointResolver>(
            new StaticServiceEndpointResolver(new Dictionary<string, Uri>()));

        Action act = () => services.AddStaticServiceDiscovery(new Dictionary<string, Uri>());

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*StaticServiceEndpointResolver*already registered*");
    }

    [Fact]
    public void AddStaticServiceDiscovery_ThrowsInvalidOperationException_AfterAddK8s()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddK8sServiceDiscovery();

        Action act = () => services.AddStaticServiceDiscovery(new Dictionary<string, Uri>());

        act.Should().Throw<InvalidOperationException>();
    }

    // ─────────────────────────────────────────────────────────────
    // T-36 (P-360/WO-056): the registration guard is symmetric —
    // whichever service-discovery extension runs second throws, regardless of call order.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddK8sServiceDiscovery_ThrowsInvalidOperationException_AfterAddStaticServiceDiscovery()
    {
        // Arrange — this is the newly-fixed direction (T-07's sibling): AddK8sServiceDiscovery used
        // to silently no-op via TryAddSingleton here instead of throwing.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStaticServiceDiscovery(new Dictionary<string, Uri>());

        // Act
        Action act = () => services.AddK8sServiceDiscovery();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*KubernetesServiceEndpointResolver*already registered*");
    }

    [Fact]
    public void AddK8sServiceDiscovery_ThrowsInvalidOperationException_AfterAddK8sServiceDiscovery()
    {
        // Arrange — same-direction double-registration must also fail loudly, not silently no-op.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddK8sServiceDiscovery();

        // Act
        Action act = () => services.AddK8sServiceDiscovery();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task AddStaticServiceDiscovery_RegistersResolver_WhenNotAlreadyRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStaticServiceDiscovery(new Dictionary<string, Uri>
        {
            ["test-service"] = new Uri("http://localhost:9999")
        });

        await using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IServiceEndpointResolver>();

        resolver.Should().NotBeNull();
        resolver.Should().BeOfType<StaticServiceEndpointResolver>();
    }

    [Fact]
    public async Task AddStaticServiceDiscovery_StartupWarning_LogsAtWarningLevel()
    {
        // Arrange — capture logs via a test log sink
        var services = new ServiceCollection();
        var sink = new TestLogSink();
        services.AddLogging(b => b.AddProvider(new TestLoggerProvider(sink)));
        services.AddStaticServiceDiscovery(new Dictionary<string, Uri>
        {
            ["svc1"] = new Uri("http://localhost:1111")
        });

        await using var sp = services.BuildServiceProvider();

        // Act — start all hosted services to trigger the startup warning
        var hostedServices = sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        foreach (var svc in hostedServices)
            await svc.StartAsync(CancellationToken.None);

        // Assert — at least one Warning log was emitted, carrying the retrofitted EventId 11308
        // (P-255/WO-041: LogStaticServiceDiscoveryActive, converted from a hand-written
        // LoggerMessage.Define<int> delegate at local EventId 100 — no behavioral change).
        sink.Entries.Should().Contain(e => e.LogLevel == LogLevel.Warning && e.EventId.Id == 11308);
    }

    // ─────────────────────────────────────────────────────────────
    // AddK8sServiceDiscovery — registration smoke tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddK8sServiceDiscovery_RegistersIServiceEndpointResolver()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddK8sServiceDiscovery();

        await using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IServiceEndpointResolver>();

        resolver.Should().NotBeNull();
        resolver.Should().BeOfType<KubernetesServiceEndpointResolver>();
    }

    [Fact]
    public async Task AddK8sServiceDiscovery_WithConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddK8sServiceDiscovery(opts =>
        {
            opts.Namespace = "production";
            opts.ClusterDomain = "cluster.local";
        });

        await using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<K8sServiceDiscoveryOptions>>().Value;

        opts.Namespace.Should().Be("production");
        opts.ClusterDomain.Should().Be("cluster.local");
    }
}

// ─────────────────────────────────────────────────────────────────
// Test infrastructure — minimal log sink
// ─────────────────────────────────────────────────────────────────

/// <summary>Captures log entries for assertion.</summary>
internal sealed class TestLogSink
{
    public List<LogEntry> Entries { get; } = [];

    public void Write(LogLevel logLevel, EventId eventId, string message) =>
        Entries.Add(new LogEntry(logLevel, eventId, message));
}

internal sealed record LogEntry(LogLevel LogLevel, EventId EventId, string Message);

internal sealed class TestLoggerProvider(TestLogSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, sink);
    public void Dispose() { }
}

internal sealed class TestLogger(string categoryName, TestLogSink sink) : ILogger
{
    // categoryName is part of the ILogger contract; not used in this minimal test sink
    private readonly string _category = categoryName;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        sink.Write(logLevel, eventId, message);
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}
