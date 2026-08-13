using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="SecurityArchitectureRules"/> — covering both predicates:
/// <c>DomainNeverReferencesTenantProvider</c> and
/// <c>NoSingletonRegistrationOfSecurityContextTypes</c>.
/// </summary>
/// <remarks>
/// T-286/T-287: Rule 1 — <c>DomainNeverReferencesTenantProvider</c> (contrived fire/pass paths).
/// T-288/T-289: Rule 2 — <c>NoSingletonRegistrationOfSecurityContextTypes</c> (contrived fire/pass
/// paths).
/// T-290/T-291: real-assembly, non-gating verification against the real, shipped
/// <c>SharedKernel.Domain</c> and <c>SharedKernel.Security.Oidc</c> assemblies — both packages
/// were already fully Published as of this phase's authoring, so real-assembly verification is
/// wired directly rather than deferred as a follow-up.
/// </remarks>
public class SecurityArchitectureRulesTests
{
    // ---------------------------------------------------------------------------
    // T-286 — Rule 1 fire path: 03.Domain-shaped fixture accepts ITenantProvider as a ctor param
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-286: A fixture type accepting
    /// <c>SharedKernel.Security.Abstractions.Abstractions.ITenantProvider</c> as a constructor
    /// parameter must fail <see cref="SecurityArchitectureRules.DomainNeverReferencesTenantProvider"/>.
    /// </summary>
    [Fact]
    public void DomainNeverReferencesTenantProvider_CtorParameterViolation_RuleFails()
    {
        const string source = """
            namespace SharedKernel.Security.Abstractions.Abstractions
            {
                // Stub simulating the real, shipped ITenantProvider — same namespace and name so
                // the exact-FullName match fires correctly.
                public interface ITenantProvider
                {
                    System.Guid TenantId { get; }
                }
            }

            namespace Domain.Pricing
            {
                // Violation: a domain type directly accepts ITenantProvider as a constructor
                // parameter, instead of receiving a Guid tenantId primitive from the application
                // layer.
                public sealed class PricingPolicy
                {
                    private readonly SharedKernel.Security.Abstractions.Abstractions.ITenantProvider _tenantProvider;

                    public PricingPolicy(
                        SharedKernel.Security.Abstractions.Abstractions.ITenantProvider tenantProvider)
                    {
                        _tenantProvider = tenantProvider;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("DomainTenantProviderViolation", source);

        var result = SecurityArchitectureRules
            .DomainNeverReferencesTenantProvider(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "PricingPolicy accepts ITenantProvider directly as a constructor parameter — " +
                     "03.Domain must never reference ITenantProvider, only a Guid tenantId primitive");
    }

    // ---------------------------------------------------------------------------
    // T-287 — Rule 1 pass path: equivalent fixture accepts a Guid tenantId primitive instead
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-287: The equivalent fixture type accepting a <see cref="System.Guid"/>
    /// <c>tenantId</c> primitive instead of <c>ITenantProvider</c> must pass
    /// <see cref="SecurityArchitectureRules.DomainNeverReferencesTenantProvider"/>.
    /// </summary>
    [Fact]
    public void DomainNeverReferencesTenantProvider_GuidTenantIdPrimitive_RulePasses()
    {
        const string source = """
            namespace SharedKernel.Security.Abstractions.Abstractions
            {
                public interface ITenantProvider
                {
                    System.Guid TenantId { get; }
                }
            }

            namespace Domain.Pricing
            {
                // Compliant: the application layer resolves ITenantProvider.TenantId and passes
                // it as a Guid primitive — no reference to ITenantProvider anywhere in this type.
                public sealed class PricingPolicy
                {
                    private readonly System.Guid _tenantId;

                    public PricingPolicy(System.Guid tenantId)
                    {
                        _tenantId = tenantId;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("DomainTenantIdPrimitiveCompliant", source);

        var result = SecurityArchitectureRules
            .DomainNeverReferencesTenantProvider(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "PricingPolicy accepts a Guid tenantId primitive — no reference to " +
                     "ITenantProvider is present anywhere in the fixture assembly");
    }

    // ---------------------------------------------------------------------------
    // T-288 — Rule 2 fire path: AddSingleton<IUserContext, FixtureUserContext>() registration
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-288: A fixture DI-extension method calling
    /// <c>services.AddSingleton&lt;IUserContext, FixtureUserContext&gt;()</c> must fail
    /// <see cref="SecurityArchitectureRules.NoSingletonRegistrationOfSecurityContextTypes"/>.
    /// </summary>
    [Fact]
    public void NoSingletonRegistrationOfSecurityContextTypes_AddSingletonUserContext_RuleFails()
    {
        const string source = """
            namespace Stub.DependencyInjection
            {
                // Minimal stub mirroring Microsoft.Extensions.DependencyInjection's
                // ServiceCollectionServiceExtensions — the predicate matches on the generic
                // method's name ("AddSingleton") and generic arguments, not its declaring type,
                // so a self-contained stub is sufficient (no real DI package reference needed).
                public interface IServiceCollection { }

                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddSingleton<TService, TImplementation>(
                        this IServiceCollection services)
                        where TService : class
                        where TImplementation : class, TService
                        => services;

                    public static IServiceCollection AddScoped<TService, TImplementation>(
                        this IServiceCollection services)
                        where TService : class
                        where TImplementation : class, TService
                        => services;
                }
            }

            namespace SharedKernel.Security.Abstractions.Abstractions
            {
                public interface IUserContext { }
            }

            namespace Fixture.Composition
            {
                public sealed class FixtureUserContext
                    : SharedKernel.Security.Abstractions.Abstractions.IUserContext { }

                public static class DiExtensions
                {
                    // Violation: registers IUserContext as a singleton — one instance would be
                    // captured and leaked across every subsequent request on the same process.
                    // Called via the static form (not extension-method dot-syntax) since this
                    // fixture source deliberately omits a "using Stub.DependencyInjection;"
                    // directive — the compiled IL shape (a Call to a closed GenericInstanceMethod
                    // named "AddSingleton") is identical either way, which is exactly what the
                    // predicate under test inspects.
                    public static Stub.DependencyInjection.IServiceCollection Register(
                        Stub.DependencyInjection.IServiceCollection services)
                    {
                        return Stub.DependencyInjection.ServiceCollectionExtensions.AddSingleton<
                            SharedKernel.Security.Abstractions.Abstractions.IUserContext,
                            FixtureUserContext>(services);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SingletonUserContextViolation", source);

        var result = SecurityArchitectureRules
            .NoSingletonRegistrationOfSecurityContextTypes(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "DiExtensions.Register() calls AddSingleton<IUserContext, FixtureUserContext>() — " +
                     "IUserContext must never be registered as singleton");
    }

    // ---------------------------------------------------------------------------
    // T-289 — Rule 2 pass path: AddScoped<IUserContext, FixtureUserContext>() registration
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-289: The equivalent fixture calling
    /// <c>services.AddScoped&lt;IUserContext, FixtureUserContext&gt;()</c> must pass
    /// <see cref="SecurityArchitectureRules.NoSingletonRegistrationOfSecurityContextTypes"/>.
    /// </summary>
    [Fact]
    public void NoSingletonRegistrationOfSecurityContextTypes_AddScopedUserContext_RulePasses()
    {
        const string source = """
            namespace Stub.DependencyInjection
            {
                public interface IServiceCollection { }

                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddSingleton<TService, TImplementation>(
                        this IServiceCollection services)
                        where TService : class
                        where TImplementation : class, TService
                        => services;

                    public static IServiceCollection AddScoped<TService, TImplementation>(
                        this IServiceCollection services)
                        where TService : class
                        where TImplementation : class, TService
                        => services;
                }
            }

            namespace SharedKernel.Security.Abstractions.Abstractions
            {
                public interface IUserContext { }
            }

            namespace Fixture.Composition
            {
                public sealed class FixtureUserContext
                    : SharedKernel.Security.Abstractions.Abstractions.IUserContext { }

                public static class DiExtensions
                {
                    // Compliant: registers IUserContext as Scoped — one instance per HTTP request.
                    public static Stub.DependencyInjection.IServiceCollection Register(
                        Stub.DependencyInjection.IServiceCollection services)
                    {
                        return Stub.DependencyInjection.ServiceCollectionExtensions.AddScoped<
                            SharedKernel.Security.Abstractions.Abstractions.IUserContext,
                            FixtureUserContext>(services);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("ScopedUserContextCompliant", source);

        var result = SecurityArchitectureRules
            .NoSingletonRegistrationOfSecurityContextTypes(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "DiExtensions.Register() calls AddScoped<IUserContext, FixtureUserContext>() — " +
                     "AddScoped is the compliant registration lifetime");
    }

    // ---------------------------------------------------------------------------
    // T-290 — Real-assembly verification (non-gating): SharedKernel.Domain
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-290: Re-points <see cref="SecurityArchitectureRules.DomainNeverReferencesTenantProvider"/>
    /// at the real, shipped <c>SharedKernel.Domain</c> assembly and confirms zero violations.
    /// Non-gating — <c>03.Domain</c> was already fully Published (v1.7.0, WO-051) as of this
    /// phase's authoring.
    /// </summary>
    [Fact]
    public void DomainNeverReferencesTenantProvider_RealDomainAssembly_RulePasses()
    {
        var domainAssembly = typeof(SharedKernel.Domain.Abstractions.IDomainService).Assembly;

        var result = SecurityArchitectureRules
            .DomainNeverReferencesTenantProvider(domainAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real, shipped SharedKernel.Domain assembly never references " +
                     "SharedKernel.Security.Abstractions.Abstractions.ITenantProvider — " +
                     "03.Domain has no dependency on 12.Security at all");
    }

    // ---------------------------------------------------------------------------
    // T-291 — Real-assembly verification (non-gating): SharedKernel.Security.Oidc
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-291: Re-points
    /// <see cref="SecurityArchitectureRules.NoSingletonRegistrationOfSecurityContextTypes"/> at
    /// the real, shipped <c>SharedKernel.Security.Oidc</c> assembly (its own
    /// <c>AddSharedKernelSecurity</c>/<c>AddAzureB2CAuthentication</c> registration methods) and
    /// confirms zero violations. Non-gating — <c>12.Security</c> was already fully Published as
    /// of this phase's authoring; both registration methods register
    /// <c>IUserContext</c>/<c>ITenantProvider</c> as <c>AddScoped</c>, never
    /// <c>AddSingleton</c>.
    /// </summary>
    [Fact]
    public void NoSingletonRegistrationOfSecurityContextTypes_RealOidcAssembly_RulePasses()
    {
        var oidcAssembly = typeof(SharedKernel.Security.Oidc.Extensions.SecurityServiceCollectionExtensions).Assembly;

        var result = SecurityArchitectureRules
            .NoSingletonRegistrationOfSecurityContextTypes(oidcAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real, shipped SecurityServiceCollectionExtensions registers both " +
                     "IUserContext and ITenantProvider via AddScoped — never AddSingleton");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var refList = new System.Collections.Generic.List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Threading.Tasks").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: refList,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{assemblyName}_{System.Guid.NewGuid():N}.dll");

        using (var fs = System.IO.File.OpenWrite(tempPath))
        {
            var emitResult = compilation.Emit(fs);
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
        }

        return Assembly.LoadFrom(tempPath);
    }
}
