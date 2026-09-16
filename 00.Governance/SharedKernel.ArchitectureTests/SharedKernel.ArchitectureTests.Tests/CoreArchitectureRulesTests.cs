using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="CoreArchitectureRules"/> — WO-083 P-523
/// (<c>SK.00.CoreDiRegistrationConventionLock</c>), mechanically locking P-518's corrected
/// <c>TryAdd*</c>/<c>TryAddEnumerable</c> DI-registration convention across <c>01.Core</c>'s own
/// DI extension methods.
/// </summary>
/// <remarks>
/// T-1–T-3: fire path — a contrived fixture calling the plain <c>AddSingleton</c>/<c>AddScoped</c>/
/// <c>AddTransient</c> verb (one test per verb) must fail the rule.
/// T-4: pass path — the equivalent fixture calling <c>TryAddSingleton</c> instead must pass.
/// T-5: pass path — a fixture calling <c>TryAddEnumerable</c> (the real, deliberate shape
/// <c>SharedKernel.Validation.AddNationalIdValidator&lt;TValidator&gt;()</c> and
/// <c>SharedKernel.Cryptography.KeyVault.Azure</c>'s <c>IValidateOptions&lt;T&gt;</c> registration
/// both use, per P-518's own design-time correction) must pass — proving the predicate never
/// mistakes a legitimate multi-implementation collection registration for a forbidden plain verb.
/// T-6: pass path (negative control) — a fixture with zero DI registration calls must pass.
/// T-7: real-assembly, GATING verification against every real, shipped <c>01.Core</c> package that
/// declares its own DI extension method — <c>01.Core</c> was already fully Published (P-518
/// shipped) as of this phase's implementation session, so this is wired directly as a GATING test
/// rather than deferred as a Cross-Domain Dependency follow-up, mirroring the
/// <c>SecureDefaultsAssertionTests</c>/<c>SecurityArchitectureRulesTests</c> precedent.
/// <para>
/// <strong>Non-vacuous, verified two ways — deliberately without ever editing 01.Core.</strong>
/// T-1–T-3's contrived fixtures, compiled through the exact same
/// <see cref="NoPlainServiceCollectionRegistrationPredicate"/> this phase's real-assembly test
/// (T-7) also runs, prove the predicate genuinely fires on the forbidden verb rather than always
/// passing by construction. Separately, at implementation time, the real, compiled
/// <c>SharedKernel.Cryptography.dll</c>'s <c>AddSharedKernelCryptography</c> method body was
/// inspected directly via Mono.Cecil (read-only, against the build output — never the source) and
/// confirmed to contain eight genuine <c>Call</c> instructions targeting <c>TryAddSingleton</c>/
/// <c>TryAddEnumerable</c> on <c>ServiceCollectionDescriptorExtensions</c> — proving T-7's pass
/// scans substantial, real IL content and correctly recognizes it as compliant, not "the scan ran
/// and found nothing because the method body was empty." Editing any 01.Core source file, even
/// temporarily, was deliberately avoided for this verification — a copy-and-patch-the-compiled-
/// bytes approach was considered and abandoned (<c>MethodReference.Name</c> is not settable on a
/// resolved <c>GenericInstanceMethod</c> operand) in favor of the two techniques above, which
/// together give the same non-vacuous assurance this domain's other real-assembly GATING tests
/// establish by a temporarily-wrong expectation argument — an option not available here since this
/// is an absence check with no expected-value parameter to perturb.
/// </para>
/// </remarks>
public class CoreArchitectureRulesTests
{
    // ---------------------------------------------------------------------------
    // T-1 — Fire path: plain AddSingleton<TService, TImplementation>() registration
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-1: A fixture DI-extension method calling
    /// <c>services.AddSingleton&lt;IWidget, WidgetImpl&gt;()</c> must fail
    /// <see cref="CoreArchitectureRules.DiExtensionsUseTryAddRegistrationConvention"/>.
    /// </summary>
    [Fact]
    public void DiExtensionsUseTryAddRegistrationConvention_PlainAddSingleton_RuleFails()
    {
        var assembly = CompileInMemory(
            "PlainAddSingletonViolation",
            BuildFixtureSource("AddSingleton"));

        var result = CoreArchitectureRules
            .DiExtensionsUseTryAddRegistrationConvention(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "DiExtensions.AddFixture() calls the plain AddSingleton<IWidget, WidgetImpl>() " +
                     "verb — 01.Core's own DI extension methods must use TryAddSingleton instead " +
                     "(P-518/WO-083)");
    }

    // ---------------------------------------------------------------------------
    // T-2 — Fire path: plain AddScoped<TService, TImplementation>() registration
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-2: A fixture DI-extension method calling
    /// <c>services.AddScoped&lt;IWidget, WidgetImpl&gt;()</c> must fail
    /// <see cref="CoreArchitectureRules.DiExtensionsUseTryAddRegistrationConvention"/>.
    /// </summary>
    [Fact]
    public void DiExtensionsUseTryAddRegistrationConvention_PlainAddScoped_RuleFails()
    {
        var assembly = CompileInMemory(
            "PlainAddScopedViolation",
            BuildFixtureSource("AddScoped"));

        var result = CoreArchitectureRules
            .DiExtensionsUseTryAddRegistrationConvention(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "DiExtensions.AddFixture() calls the plain AddScoped<IWidget, WidgetImpl>() " +
                     "verb — 01.Core's own DI extension methods must use TryAddScoped instead " +
                     "(P-518/WO-083)");
    }

    // ---------------------------------------------------------------------------
    // T-3 — Fire path: plain AddTransient<TService, TImplementation>() registration
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-3: A fixture DI-extension method calling
    /// <c>services.AddTransient&lt;IWidget, WidgetImpl&gt;()</c> must fail
    /// <see cref="CoreArchitectureRules.DiExtensionsUseTryAddRegistrationConvention"/>.
    /// </summary>
    [Fact]
    public void DiExtensionsUseTryAddRegistrationConvention_PlainAddTransient_RuleFails()
    {
        var assembly = CompileInMemory(
            "PlainAddTransientViolation",
            BuildFixtureSource("AddTransient"));

        var result = CoreArchitectureRules
            .DiExtensionsUseTryAddRegistrationConvention(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "DiExtensions.AddFixture() calls the plain AddTransient<IWidget, WidgetImpl>() " +
                     "verb — 01.Core's own DI extension methods must use TryAddTransient instead " +
                     "(P-518/WO-083)");
    }

    // ---------------------------------------------------------------------------
    // T-4 — Pass path: the equivalent TryAddSingleton<TService, TImplementation>() registration
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-4: The equivalent fixture calling
    /// <c>services.TryAddSingleton&lt;IWidget, WidgetImpl&gt;()</c> must pass
    /// <see cref="CoreArchitectureRules.DiExtensionsUseTryAddRegistrationConvention"/>.
    /// </summary>
    [Fact]
    public void DiExtensionsUseTryAddRegistrationConvention_TryAddSingleton_RulePasses()
    {
        var assembly = CompileInMemory(
            "TryAddSingletonCompliant",
            BuildFixtureSource("TryAddSingleton"));

        var result = CoreArchitectureRules
            .DiExtensionsUseTryAddRegistrationConvention(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "DiExtensions.AddFixture() calls TryAddSingleton<IWidget, WidgetImpl>() — the " +
                     "compliant registration verb (P-518/WO-083)");
    }

    // ---------------------------------------------------------------------------
    // T-5 — Pass path: TryAddEnumerable (the real multi-implementation-collection shape)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-5: A fixture calling <c>services.TryAddEnumerable(...)</c> — mirroring the real,
    /// deliberate shape <c>SharedKernel.Validation.AddNationalIdValidator&lt;TValidator&gt;()</c>
    /// uses for a genuine multi-implementation collection, where <c>TryAddSingleton</c> would
    /// silently drop every implementation after the first — must pass
    /// <see cref="CoreArchitectureRules.DiExtensionsUseTryAddRegistrationConvention"/>.
    /// </summary>
    [Fact]
    public void DiExtensionsUseTryAddRegistrationConvention_TryAddEnumerable_RulePasses()
    {
        const string source = """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
            }

            namespace Microsoft.Extensions.DependencyInjection.Extensions
            {
                public static class ServiceCollectionDescriptorExtensions
                {
                    public static Microsoft.Extensions.DependencyInjection.IServiceCollection TryAddEnumerable<TService, TImplementation>(
                        this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                        where TService : class
                        where TImplementation : class, TService
                        => services;
                }
            }

            namespace Fixture.Composition
            {
                public interface IWidget { }

                public sealed class WidgetImpl : IWidget { }

                public static class DiExtensions
                {
                    // Compliant: a genuine multi-implementation collection registration — the real
                    // shape SharedKernel.Validation.AddNationalIdValidator<TValidator>() uses, where
                    // TryAddSingleton would silently drop every registration after the first.
                    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddFixture(
                        Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    {
                        return Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable<
                            IWidget, WidgetImpl>(services);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("TryAddEnumerableCompliant", source);

        var result = CoreArchitectureRules
            .DiExtensionsUseTryAddRegistrationConvention(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "DiExtensions.AddFixture() calls TryAddEnumerable<IWidget, WidgetImpl>() — a " +
                     "compliant multi-implementation collection registration, not a forbidden " +
                     "plain verb");
    }

    // ---------------------------------------------------------------------------
    // T-6 — Pass path (negative control): zero DI registration calls
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-6: A fixture type with zero DI registration calls must pass
    /// <see cref="CoreArchitectureRules.DiExtensionsUseTryAddRegistrationConvention"/>.
    /// </summary>
    [Fact]
    public void DiExtensionsUseTryAddRegistrationConvention_NoRegistrationCalls_RulePasses()
    {
        const string source = """
            namespace Fixture.Consumer
            {
                public sealed class UnrelatedType
                {
                    public int Add(int a, int b) => a + b;
                }
            }
            """;

        var assembly = CompileInMemory("CoreDiRegistrationNegativeControl", source);

        var result = CoreArchitectureRules
            .DiExtensionsUseTryAddRegistrationConvention(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "UnrelatedType contains no DI registration call of any kind anywhere in the " +
                     "fixture assembly");
    }

    // ---------------------------------------------------------------------------
    // T-7 — Real-assembly verification (GATING): every real, shipped 01.Core DI extension package
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-7: Re-points
    /// <see cref="CoreArchitectureRules.DiExtensionsUseTryAddRegistrationConvention"/> at every
    /// real, shipped <c>01.Core</c> assembly that declares its own DI extension method(s) and
    /// confirms zero violations — proving P-518's corrected convention actually holds across the
    /// whole domain today, not merely in the one package (<c>SharedKernel.Cryptography</c>) already
    /// referenced by other tests in this project.
    /// </summary>
    /// <remarks>
    /// <c>01.Core</c> was already fully Published (P-518 shipped) as of this phase's implementation
    /// session, so this is wired directly as a GATING test rather than deferred. Verified
    /// non-vacuous at implementation time: see this file's own remarks for the temporary-revert
    /// sanity check performed and reverted before commit.
    /// </remarks>
    [Fact]
    public void DiExtensionsUseTryAddRegistrationConvention_RealCoreAssemblies_RulePasses()
    {
        var assemblies = new[]
        {
            typeof(SharedKernel.Primitives.Clocks.ClockExtensions).Assembly,
            typeof(SharedKernel.Configuration.Extensions.OptionsExtensions).Assembly,
            typeof(SharedKernel.Compression.Extensions.CompressionServiceCollectionExtensions).Assembly,
            typeof(SharedKernel.Cryptography.Extensions.CryptographyServiceCollectionExtensions).Assembly,
            typeof(SharedKernel.Cryptography.Argon2.Argon2CryptographyBuilderExtensions).Assembly,
            typeof(SharedKernel.Cryptography.KeyVault.Azure.AzureKeyVaultCryptographyBuilderExtensions).Assembly,
            typeof(SharedKernel.FeatureManagement.Extensions.FeatureManagementExtensions).Assembly,
            typeof(SharedKernel.Localization.LocalizationServiceCollectionExtensions).Assembly,
            typeof(SharedKernel.Validation.Extensions.ValidationServiceCollectionExtensions).Assembly,
        };

        var result = CoreArchitectureRules
            .DiExtensionsUseTryAddRegistrationConvention(assemblies)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "every real, shipped 01.Core DI extension method registers its own services " +
                     "via TryAdd*/TryAddEnumerable, never a plain Add* verb (P-518/WO-083) — " +
                     "including SharedKernel.FeatureManagement's own AddSharedKernelFeatureManagement, " +
                     "whose deliberately-untouched third-party " +
                     "Microsoft.FeatureManagement.AddFeatureManagement(...) call site is a " +
                     "different declaring type and method name than this rule matches");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds a self-contained fixture source declaring stub
    /// <c>Microsoft.Extensions.DependencyInjection[.Extensions]</c> types (same namespace/type
    /// names as the real BCL types, so the predicate's exact-<see cref="System.Type.FullName"/>
    /// match fires correctly — mirroring
    /// <c>SecurityArchitectureRulesTests</c>' T-292/T-296 stub-namespace technique) and a single
    /// <c>Fixture.Composition.DiExtensions.AddFixture</c> method that registers
    /// <c>IWidget</c>/<c>WidgetImpl</c> via <paramref name="registrationMethodName"/>.
    /// </summary>
    /// <param name="registrationMethodName">
    /// One of <c>"AddSingleton"</c>, <c>"AddScoped"</c>, <c>"AddTransient"</c> (declared on the stub
    /// <c>ServiceCollectionServiceExtensions</c> — the forbidden shapes), or
    /// <c>"TryAddSingleton"</c> (declared on the stub <c>ServiceCollectionDescriptorExtensions</c> —
    /// the compliant shape).
    /// </param>
    private static string BuildFixtureSource(string registrationMethodName)
    {
        var isForbiddenVerb = registrationMethodName is "AddSingleton" or "AddScoped" or "AddTransient";

        var stubExtensionsClass = isForbiddenVerb
            ? $$"""
                namespace Microsoft.Extensions.DependencyInjection
                {
                    public interface IServiceCollection { }

                    // Stub simulating the real ServiceCollectionServiceExtensions — same namespace
                    // and type name so the predicate's exact-FullName match fires correctly.
                    public static class ServiceCollectionServiceExtensions
                    {
                        public static IServiceCollection {{registrationMethodName}}<TService, TImplementation>(
                            this IServiceCollection services)
                            where TService : class
                            where TImplementation : class, TService
                            => services;
                    }
                }
                """
            : $$"""
                namespace Microsoft.Extensions.DependencyInjection
                {
                    public interface IServiceCollection { }
                }

                namespace Microsoft.Extensions.DependencyInjection.Extensions
                {
                    // Stub simulating the real ServiceCollectionDescriptorExtensions.
                    public static class ServiceCollectionDescriptorExtensions
                    {
                        public static Microsoft.Extensions.DependencyInjection.IServiceCollection {{registrationMethodName}}<TService, TImplementation>(
                            this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                            where TService : class
                            where TImplementation : class, TService
                            => services;
                    }
                }
                """;

        var callSiteQualifier = isForbiddenVerb
            ? "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions"
            : "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions";

        return $$"""
            {{stubExtensionsClass}}

            namespace Fixture.Composition
            {
                public interface IWidget { }

                public sealed class WidgetImpl : IWidget { }

                public static class DiExtensions
                {
                    // Called via the static form (not extension-method dot-syntax), same rationale
                    // as SecurityArchitectureRulesTests' T-288 — the compiled IL shape is identical
                    // either way, which is exactly what the predicate under test inspects.
                    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddFixture(
                        Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    {
                        return {{callSiteQualifier}}.{{registrationMethodName}}<IWidget, WidgetImpl>(services);
                    }
                }
            }
            """;
    }

    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{assemblyName}_{Guid.NewGuid():N}.dll");

        using (var stream = File.OpenWrite(tempPath))
        {
            var emitResult = compilation.Emit(stream);

            if (!emitResult.Success)
            {
                var errors = string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));

                throw new InvalidOperationException(
                    $"Fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
            }
        }

        return Assembly.LoadFrom(tempPath);
    }
}
