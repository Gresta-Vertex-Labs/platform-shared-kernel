using System;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0033 <see cref="ReflectionBasedObjectMapperUsageAnalyzer"/>.</summary>
/// <remarks>
/// T-341: Fire path — an <c>AutoMapper.Profile</c> subclass.
/// T-342: Fire path — an <c>.AddAutoMapper(...)</c> DI registration call.
/// T-343: Fire path — an <c>IMapperConfigurationExpression</c>-typed parameter usage.
/// T-344: Pass path — a <c>Riok.Mapperly</c> <c>[Mapper]</c> partial class.
/// T-345: Pass path — a plain hand-written static mapping method.
/// <para>
/// Every test references the REAL <c>AutoMapper</c>/<c>Riok.Mapperly.Abstractions</c> assemblies
/// (via this test project's own compile-time <c>PackageReference</c>s) rather than an
/// in-compilation stand-in — SK0033's own design requires exact
/// <see cref="ISymbol.ContainingAssembly"/> resolution, which only the real assembly can prove.
/// </para>
/// </remarks>
public class SK0033_ReflectionBasedObjectMapperUsageAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-341 — Fire path: AutoMapper.Profile subclass
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_AutoMapperProfileSubclass_ReportsDiagnostic()
    {
        var test = CreateTest(
            """
            using AutoMapper;

            namespace Fixture
            {
                public class Source
                {
                    public string Name { get; set; } = string.Empty;
                }

                public class Destination
                {
                    public string Name { get; set; } = string.Empty;
                }

                public class CustomerProfile : {|SK0033:Profile|}
                {
                    public CustomerProfile()
                    {
                        CreateMap<Source, Destination>();
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-342 — Fire path: .AddAutoMapper(...) DI registration call
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_AddAutoMapperRegistration_ReportsDiagnostic()
    {
        var test = CreateTest(
            """
            using System.Reflection;
            using AutoMapper;
            using Microsoft.Extensions.DependencyInjection;

            namespace Fixture
            {
                public static class Startup
                {
                    public static void ConfigureServices(IServiceCollection services)
                    {
                        {|SK0033:services.AddAutoMapper(cfg => { }, System.Array.Empty<Assembly>())|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-343 — Fire path: IMapperConfigurationExpression parameter usage
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_MapperConfigurationExpressionParameter_ReportsDiagnostic()
    {
        var test = CreateTest(
            """
            using AutoMapper;

            namespace Fixture
            {
                public static class MappingConfigurator
                {
                    public static void Configure({|SK0033:IMapperConfigurationExpression|} cfg)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-344 — Pass path: Riok.Mapperly [Mapper] partial class
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_MapperlyPartialClass_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Riok.Mapperly.Abstractions;

            namespace Fixture
            {
                public class Source
                {
                    public string Name { get; set; } = string.Empty;
                }

                public class Destination
                {
                    public string Name { get; set; } = string.Empty;
                }

                [Mapper]
                public partial class CustomerMapper
                {
                    // The real Riok.Mapperly source generator would normally implement a `partial`
                    // method here; this fixture omits the generator (no `Analyzer`/generator
                    // reference is added, only the `Riok.Mapperly.Abstractions` metadata reference
                    // for the [Mapper] attribute) and provides a plain hand-written body instead —
                    // SK0033's own scope is proving [Mapper] does not itself trigger the rule, not
                    // exercising Mapperly's code generation.
                    public Destination Map(Source source) => new Destination { Name = source.Name };
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-345 — Pass path: plain hand-written static mapping method
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_HandWrittenStaticMappingMethod_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture
            {
                public class Source
                {
                    public string Name { get; set; } = string.Empty;
                }

                public class Destination
                {
                    public string Name { get; set; } = string.Empty;
                }

                public static class CustomerMapper
                {
                    public static Destination Map(Source source) =>
                        new Destination { Name = source.Name };
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // Test construction helper
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="CSharpAnalyzerTest{TAnalyzer, TVerifier}"/> for
    /// <see cref="ReflectionBasedObjectMapperUsageAnalyzer"/> against the .NET 8.0 reference
    /// assembly set, with real metadata references to the compiled <c>AutoMapper</c>,
    /// <c>Riok.Mapperly.Abstractions</c>, and <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>
    /// assemblies, per SK0022's established <see cref="ReferenceAssemblies.Net.Net80"/> +
    /// <see cref="MetadataReference.CreateFromFile(string)"/> pattern.
    /// </summary>
    /// <remarks>
    /// Deliberately loads the <c>netstandard2.0</c> asset of each package from the local NuGet
    /// global-packages cache (<see cref="ResolveNuGetAssembly"/>) rather than
    /// <c>typeof(AutoMapper.Profile).Assembly.Location</c> — the latter resolves whichever
    /// TFM-specific asset NuGet selected for THIS net10.0 test project (the <c>net10.0</c> asset,
    /// referencing <c>System.Runtime, Version=10.0.0.0</c>), which is a strictly higher version than
    /// <see cref="ReferenceAssemblies.Net.Net80"/>'s own <c>System.Runtime</c> reference assembly and
    /// produces a CS1705 assembly-version-mismatch compiler error inside the isolated test
    /// compilation. The <c>netstandard2.0</c> asset has no such requirement and compiles cleanly
    /// against the .NET 8.0 reference set.
    /// </remarks>
    private static CSharpAnalyzerTest<ReflectionBasedObjectMapperUsageAnalyzer, DefaultVerifier> CreateTest(
        string source
    )
    {
        var test = new CSharpAnalyzerTest<ReflectionBasedObjectMapperUsageAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.TestState.AdditionalReferences.Add(
            ResolveNuGetAssembly("automapper", "15.1.1", "AutoMapper.dll")
        );
        test.TestState.AdditionalReferences.Add(
            ResolveNuGetAssembly("riok.mapperly", "3.6.0", "Riok.Mapperly.Abstractions.dll")
        );
        test.TestState.AdditionalReferences.Add(
            ResolveNuGetAssembly(
                "microsoft.extensions.dependencyinjection.abstractions",
                "10.0.11",
                "Microsoft.Extensions.DependencyInjection.Abstractions.dll"
            )
        );

        return test;
    }

    /// <summary>
    /// Resolves the <c>netstandard2.0</c> asset of a NuGet package from the local global-packages
    /// cache (honoring the <c>NUGET_PACKAGES</c> environment variable, falling back to the default
    /// <c>~/.nuget/packages</c> location NuGet itself uses when that variable is unset).
    /// </summary>
    /// <remarks>
    /// The package id/version pair is hardcoded per call site rather than derived from this test
    /// assembly's own compile-time <c>PackageReference</c> version — there is no supported API to
    /// read a project's resolved package version at test run time. Keep these literals in sync with
    /// <c>SharedKernel.Analyzers.Tests.csproj</c>/<c>Directory.Packages.props</c> if either pin ever
    /// moves; a stale literal fails fast with a clear <see cref="FileNotFoundException"/>, not a
    /// silent misresolution.
    /// </remarks>
    private static MetadataReference ResolveNuGetAssembly(string packageId, string version, string assemblyFileName)
    {
        var packagesRoot =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages"
            );

        var assemblyPath = Path.Combine(
            packagesRoot,
            packageId,
            version,
            "lib",
            "netstandard2.0",
            assemblyFileName
        );

        return MetadataReference.CreateFromFile(assemblyPath);
    }
}
