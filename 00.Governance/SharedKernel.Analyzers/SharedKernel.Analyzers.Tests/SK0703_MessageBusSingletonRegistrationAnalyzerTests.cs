using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for SK0703 <see cref="MessageBusSingletonRegistrationAnalyzer"/>.
/// </summary>
public class SK0703_MessageBusSingletonRegistrationAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-92 — Fire path: AddSingleton<IMessageBus, ...>() triggers SK0703
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-92a: <c>services.AddSingleton&lt;IMessageBus, MassTransitMessageBus&gt;()</c>
    /// triggers SK0703 — IMessageBus must not be registered as Singleton.
    /// </summary>
    [Fact]
    public async Task FirePath_AddSingletonWithIMessageBus_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<MessageBusSingletonRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IMessageBus { }
                public class MassTransitMessageBus : IMessageBus { }
                public class IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddSingleton<TService, TImpl>(
                        this IServiceCollection services) => services;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|SK0703:services.AddSingleton<IMessageBus, MassTransitMessageBus>()|}; // SK0703
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-92b: <c>services.AddSingleton&lt;IEventPublisher, MassTransitEventPublisher&gt;()</c>
    /// triggers SK0703 — IEventPublisher must not be registered as Singleton.
    /// </summary>
    [Fact]
    public async Task FirePath_AddSingletonWithIEventPublisher_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<MessageBusSingletonRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IEventPublisher { }
                public class MassTransitEventPublisher : IEventPublisher { }
                public class IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddSingleton<TService, TImpl>(
                        this IServiceCollection services) => services;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|SK0703:services.AddSingleton<IEventPublisher, MassTransitEventPublisher>()|}; // SK0703
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-93 — Pass path: AddScoped<IMessageBus, ...>() does not trigger SK0703
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-93a: <c>services.AddScoped&lt;IMessageBus, MassTransitMessageBus&gt;()</c>
    /// does not trigger SK0703 — Scoped lifetime is correct for IMessageBus.
    /// </summary>
    [Fact]
    public async Task PassPath_AddScopedWithIMessageBus_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<MessageBusSingletonRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IMessageBus { }
                public class MassTransitMessageBus : IMessageBus { }
                public class IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddScoped<TService, TImpl>(
                        this IServiceCollection services) => services;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        // Compliant: AddScoped is the correct lifetime for IMessageBus
                        services.AddScoped<IMessageBus, MassTransitMessageBus>();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-93b: <c>services.AddScoped&lt;IEventPublisher, MassTransitEventPublisher&gt;()</c>
    /// does not trigger SK0703 — Scoped lifetime is correct for IEventPublisher.
    /// </summary>
    [Fact]
    public async Task PassPath_AddScopedWithIEventPublisher_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<MessageBusSingletonRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IEventPublisher { }
                public class MassTransitEventPublisher : IEventPublisher { }
                public class IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddScoped<TService, TImpl>(
                        this IServiceCollection services) => services;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        // Compliant: AddScoped is the correct lifetime for IEventPublisher
                        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: <c>AddSingleton</c> with an unrelated interface does not trigger SK0703.
    /// </summary>
    [Fact]
    public async Task PassPath_AddSingletonWithUnrelatedInterface_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<MessageBusSingletonRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IMyService { }
                public class MyServiceImpl : IMyService { }
                public class IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddSingleton<TService, TImpl>(
                        this IServiceCollection services) => services;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        // Compliant: AddSingleton on an unrelated interface is fine
                        services.AddSingleton<IMyService, MyServiceImpl>();
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}
