using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for SK0022 <see cref="CrossCuttingMagicStringLiteralAnalyzer"/> — WO-042 P-264.
/// </summary>
/// <remarks>
/// <para>
/// T-184: Fire path — raw string literal at an HTTP header indexer/<c>.Add</c>/
/// <c>.TryAddWithoutValidation</c> call triggers SK0022.
/// </para>
/// <para>T-185: Fire path — raw string literal passed to <c>Activity.SetBaggage</c>/<c>.SetTag</c>
/// triggers SK0022.</para>
/// <para>T-186: Fire path — raw string literal passed to <c>IConfiguration.GetSection</c> triggers
/// SK0022.</para>
/// <para>T-187: Fire path — raw string literal compared against <c>Claim.Type</c>/passed to
/// <c>ClaimsPrincipal.HasClaim</c>/<c>FindFirst</c> triggers SK0022.</para>
/// <para>T-188: Pass path — a named-constant/<c>static readonly</c> field reference at each of the
/// four call-site shapes does not trigger SK0022, including a fixture-local domain constants class
/// distinct from any 01.Core holder.</para>
/// <para>
/// The HTTP-header (<c>System.Net.Http.Headers.HttpHeaders</c>/<c>HttpRequestHeaders</c>),
/// <c>System.Diagnostics.Activity</c>, and <c>System.Security.Claims.*</c> shapes use the REAL BCL
/// types directly — these are inbox framework types (same tier as <c>System.Net.Http.HttpClient</c>,
/// already proven available without an explicit package reference by SK0013's own test fixtures)
/// and carry no independent NuGet version to conflict with the analyzer-testing sandbox's default
/// reference-assembly set. Only <c>Microsoft.Extensions.Configuration.IConfiguration</c> and
/// <c>Microsoft.AspNetCore.Http.IHeaderDictionary</c> — genuinely external, separately-versioned
/// packages not available to a plain classlib compilation — are stubbed in-compilation, the same
/// technique established for SK0013/SK0020/SK0021.
/// </para>
/// </remarks>
public class SK0022_CrossCuttingMagicStringLiteralAnalyzerTests
{
    /// <summary>
    /// Minimal in-compilation stub of <c>Microsoft.AspNetCore.Http.IHeaderDictionary</c> — an
    /// ASP.NET Core type unavailable to a plain classlib compilation without the web SDK.
    /// </summary>
    private const string HeaderDictionaryStub = """
        namespace Microsoft.AspNetCore.Http
        {
            public interface IHeaderDictionary
            {
                string this[string key] { get; set; }

                void Add(string key, string value);
            }
        }
        """;

    /// <summary>
    /// Minimal in-compilation stub of <c>Microsoft.Extensions.Configuration.IConfiguration</c> —
    /// a separately-versioned NuGet package not referenced by this test project.
    /// </summary>
    private const string ConfigurationStub = """
        namespace Microsoft.Extensions.Configuration
        {
            public interface IConfigurationSection
            {
            }

            public interface IConfiguration
            {
                IConfigurationSection GetSection(string key);
            }
        }
        """;

    private static CSharpAnalyzerTest<CrossCuttingMagicStringLiteralAnalyzer, DefaultVerifier> CreateTest(
        string source,
        params string[] additionalSources
    )
    {
        var test = new CSharpAnalyzerTest<CrossCuttingMagicStringLiteralAnalyzer, DefaultVerifier>();
        test.TestState.Sources.Add(source);
        foreach (var additional in additionalSources)
            test.TestState.Sources.Add(additional);
        return test;
    }

    /// <summary>
    /// Same as <see cref="CreateTest(string, string[])"/>, plus an additional metadata reference
    /// to the CURRENTLY RUNNING process's <c>System.Diagnostics.Activity</c> assembly (from this
    /// net10.0 test host). The analyzer-testing sandbox's default reference-assembly set resolves
    /// an older <c>System.Diagnostics.DiagnosticSource</c> contract that predates the
    /// <c>Activity.SetTag</c>/<c>.SetBaggage</c> fluent overloads (added in .NET 5) — adding this
    /// reference makes the modern members resolvable without redeclaring the type (which would
    /// conflict with the type already present in the default set).
    /// </summary>
    private static CSharpAnalyzerTest<CrossCuttingMagicStringLiteralAnalyzer, DefaultVerifier> CreateActivityTest(
        string source
    )
    {
        var test = CreateTest(source);
        test.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        return test;
    }

    // ---------------------------------------------------------------------------
    // T-184 — Fire path: HTTP header indexer / .Add / .TryAddWithoutValidation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_HttpHeadersAdd_ReportsSk0022()
    {
        var test = CreateTest(
            """
            using System.Net.Http;

            namespace Infrastructure.Clients
            {
                public class CorrelationHandler
                {
                    public void Apply(HttpRequestMessage request, string correlationId)
                    {
                        request.Headers.Add({|SK0022:"X-Correlation-Id"|}, correlationId);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_HttpHeadersTryAddWithoutValidation_ReportsSk0022()
    {
        var test = CreateTest(
            """
            using System.Net.Http;

            namespace Infrastructure.Clients
            {
                public class CorrelationHandler
                {
                    public void Apply(HttpRequestMessage request, string correlationId)
                    {
                        request.Headers.TryAddWithoutValidation({|SK0022:"X-Correlation-Id"|}, correlationId);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_HeaderDictionaryIndexer_ReportsSk0022()
    {
        var test = CreateTest(
            """
            using Microsoft.AspNetCore.Http;

            namespace Infrastructure.Middleware
            {
                public class CorrelationMiddleware
                {
                    public void Apply(IHeaderDictionary headers, string correlationId)
                    {
                        headers[{|SK0022:"X-Correlation-Id"|}] = correlationId;
                    }
                }
            }
            """,
            HeaderDictionaryStub
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_HeaderDictionaryAdd_ReportsSk0022()
    {
        var test = CreateTest(
            """
            using Microsoft.AspNetCore.Http;

            namespace Infrastructure.Middleware
            {
                public class CorrelationMiddleware
                {
                    public void Apply(IHeaderDictionary headers, string correlationId)
                    {
                        headers.Add({|SK0022:"X-Correlation-Id"|}, correlationId);
                    }
                }
            }
            """,
            HeaderDictionaryStub
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-185 — Fire path: Activity.SetBaggage / .SetTag
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_ActivitySetBaggage_ReportsSk0022()
    {
        var test = CreateActivityTest(
            """
            using System.Diagnostics;

            namespace Infrastructure.Tracing
            {
                public class CorrelationPropagator
                {
                    public void Apply(Activity activity, string correlationId)
                    {
                        activity.SetBaggage({|SK0022:"correlation.id"|}, correlationId);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_ActivitySetTag_ReportsSk0022()
    {
        var test = CreateActivityTest(
            """
            using System.Diagnostics;

            namespace Infrastructure.Tracing
            {
                public class CorrelationPropagator
                {
                    public void Apply(Activity activity, string correlationId)
                    {
                        activity.SetTag({|SK0022:"correlation.id"|}, correlationId);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-186 — Fire path: IConfiguration.GetSection
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_ConfigurationGetSection_ReportsSk0022()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Configuration;

            namespace Infrastructure.Options
            {
                public class OptionsLoader
                {
                    public IConfigurationSection Load(IConfiguration configuration)
                    {
                        return configuration.GetSection({|SK0022:"FeatureFlags"|});
                    }
                }
            }
            """,
            ConfigurationStub
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-187 — Fire path: ClaimsPrincipal/Claim comparison
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_ClaimTypeEqualityComparison_ReportsSk0022()
    {
        var test = CreateTest(
            """
            using System.Security.Claims;

            namespace Infrastructure.Security
            {
                public class RoleChecker
                {
                    public bool IsRole(Claim claim)
                    {
                        return claim.Type == {|SK0022:"role"|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_ClaimsPrincipalHasClaim_ReportsSk0022()
    {
        var test = CreateTest(
            """
            using System.Security.Claims;

            namespace Infrastructure.Security
            {
                public class RoleChecker
                {
                    public bool IsAdmin(ClaimsPrincipal principal)
                    {
                        return principal.HasClaim({|SK0022:"role"|}, "admin");
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_ClaimsPrincipalFindFirst_ReportsSk0022()
    {
        var test = CreateTest(
            """
            using System.Security.Claims;

            namespace Infrastructure.Security
            {
                public class RoleChecker
                {
                    public Claim? FindRole(ClaimsPrincipal principal)
                    {
                        return principal.FindFirst({|SK0022:"role"|});
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-188 — Pass path: named-constant/static readonly reference at each shape
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_HttpHeadersAddWithNamedConstant_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using System.Net.Http;

            namespace Infrastructure.Clients
            {
                public static class WellKnownHeaders
                {
                    public const string CorrelationId = "X-Correlation-Id";
                }

                public class CorrelationHandler
                {
                    public void Apply(HttpRequestMessage request, string correlationId)
                    {
                        request.Headers.Add(WellKnownHeaders.CorrelationId, correlationId);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_HeaderDictionaryIndexerWithNamedConstant_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.AspNetCore.Http;

            namespace Infrastructure.Middleware
            {
                public static class WellKnownHeaders
                {
                    public static readonly string CorrelationId = "X-Correlation-Id";
                }

                public class CorrelationMiddleware
                {
                    public void Apply(IHeaderDictionary headers, string correlationId)
                    {
                        headers[WellKnownHeaders.CorrelationId] = correlationId;
                    }
                }
            }
            """,
            HeaderDictionaryStub
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_ActivitySetBaggageWithNamedConstant_NoDiagnostic()
    {
        var test = CreateActivityTest(
            """
            using System.Diagnostics;

            namespace Infrastructure.Tracing
            {
                public static class WellKnownBaggageKeys
                {
                    public const string CorrelationId = "correlation.id";
                }

                public class CorrelationPropagator
                {
                    public void Apply(Activity activity, string correlationId)
                    {
                        activity.SetBaggage(WellKnownBaggageKeys.CorrelationId, correlationId);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_ConfigurationGetSectionWithNamedConstant_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Configuration;

            namespace Infrastructure.Options
            {
                public class FeatureFlagsOptions
                {
                    public const string SectionName = "FeatureFlags";
                }

                public class OptionsLoader
                {
                    public IConfigurationSection Load(IConfiguration configuration)
                    {
                        return configuration.GetSection(FeatureFlagsOptions.SectionName);
                    }
                }
            }
            """,
            ConfigurationStub
        );
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path proving the rule is declaring-class-agnostic: a fixture-local domain constants
    /// class named distinctly from any <c>01.Core</c> cross-cutting holder (mirroring
    /// <c>SecurityClaimTypes</c>) satisfies SK0022 exactly as well as a reference to
    /// <c>WellKnownHeaders</c>/<c>WellKnownBaggageKeys</c> would — the analyzer never inspects
    /// which class declares the referenced field.
    /// </summary>
    [Fact]
    public async Task PassPath_ClaimsWithFixtureLocalDomainConstantsClass_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using System.Security.Claims;

            namespace Tenanting.Security
            {
                // A fixture-local domain constants class, distinct in both name and namespace
                // from any 01.Core WellKnownHeaders/WellKnownBaggageKeys holder.
                public static class TenantClaimTypes
                {
                    public const string TenantId = "tenant_id";
                }

                public class TenantResolver
                {
                    public bool IsTenantScoped(Claim claim) => claim.Type == TenantClaimTypes.TenantId;

                    public Claim? FindTenant(ClaimsPrincipal principal) =>
                        principal.FindFirst(TenantClaimTypes.TenantId);

                    public bool HasTenant(ClaimsPrincipal principal, string tenantId) =>
                        principal.HasClaim(TenantClaimTypes.TenantId, tenantId);
                }
            }
            """
        );
        await test.RunAsync();
    }
}
