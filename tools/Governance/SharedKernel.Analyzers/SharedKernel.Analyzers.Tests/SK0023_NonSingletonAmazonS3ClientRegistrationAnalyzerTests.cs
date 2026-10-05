using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for SK0023 <see cref="NonSingletonAmazonS3ClientRegistrationAnalyzer"/>.
/// </summary>
public class SK0023_NonSingletonAmazonS3ClientRegistrationAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-191 — Fire path: AddScoped<IAmazonS3>(factory) triggers SK0023
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-191: <c>services.AddScoped&lt;IAmazonS3&gt;(sp =&gt; ...)</c> (one-argument factory
    /// form) triggers SK0023 — IAmazonS3 must not be registered as Scoped.
    /// </summary>
    [Fact]
    public async Task FirePath_AddScopedWithIAmazonS3Factory_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<NonSingletonAmazonS3ClientRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IAmazonS3 { }
                public class AmazonS3Client : IAmazonS3 { }
                public class IServiceProvider { }
                public class IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddScoped<TService>(
                        this IServiceCollection services,
                        System.Func<IServiceProvider, TService> factory) => services;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|SK0023:services.AddScoped<IAmazonS3>(sp => new AmazonS3Client())|}; // SK0023
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-192 — Fire path: AddTransient<IAmazonS3, AmazonS3Client>() triggers SK0023
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-192: <c>services.AddTransient&lt;IAmazonS3, AmazonS3Client&gt;()</c> (two-argument
    /// form) triggers SK0023 — IAmazonS3 must not be registered as Transient.
    /// </summary>
    [Fact]
    public async Task FirePath_AddTransientWithIAmazonS3TwoArgumentForm_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<NonSingletonAmazonS3ClientRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IAmazonS3 { }
                public class AmazonS3Client : IAmazonS3 { }
                public class IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddTransient<TService, TImpl>(
                        this IServiceCollection services) => services;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|SK0023:services.AddTransient<IAmazonS3, AmazonS3Client>()|}; // SK0023
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-193 — Pass path: AddSingleton<IAmazonS3>(factory) does not trigger SK0023
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-193: <c>services.AddSingleton&lt;IAmazonS3&gt;(sp =&gt; ...)</c> does not trigger
    /// SK0023 — Singleton is the correct lifetime for IAmazonS3.
    /// </summary>
    [Fact]
    public async Task PassPath_AddSingletonWithIAmazonS3Factory_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<NonSingletonAmazonS3ClientRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IAmazonS3 { }
                public class AmazonS3Client : IAmazonS3 { }
                public class IServiceProvider { }
                public class IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddSingleton<TService>(
                        this IServiceCollection services,
                        System.Func<IServiceProvider, TService> factory) => services;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        // Compliant: AddSingleton is the correct lifetime for IAmazonS3
                        services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client());
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: <c>AddScoped</c> with an unrelated interface does not trigger SK0023.
    /// </summary>
    [Fact]
    public async Task PassPath_AddScopedWithUnrelatedInterface_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<NonSingletonAmazonS3ClientRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IMyService { }
                public class MyServiceImpl : IMyService { }
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
                        // Compliant: AddScoped on an unrelated interface is fine
                        services.AddScoped<IMyService, MyServiceImpl>();
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}
