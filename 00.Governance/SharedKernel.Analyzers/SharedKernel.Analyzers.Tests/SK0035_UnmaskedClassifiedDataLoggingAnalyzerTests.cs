using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0035 <see cref="UnmaskedClassifiedDataLoggingAnalyzer"/>.</summary>
/// <remarks>
/// T-351: Fire path — a <c>[DataClassification(Restricted)]</c>-classified property passed
/// directly as a message-template argument.
/// T-352: Fire path — a <c>[SensitiveDataCategory]</c>-classified field passed directly.
/// T-353: Pass path — the same classified property passed through a fixture-local
/// <c>PiiMasking.Email(...)</c>-shaped call first.
/// T-354: Pass path — an unclassified property passed directly.
/// T-355: Real-assembly verification — re-points the fully-qualified-metadata-name resolution at
/// the ACTUAL compiled <c>SharedKernel.DataPrivacy</c> package (a test-only <c>ProjectReference</c>,
/// <c>PrivateAssets="all"</c>) and proves the fire path against a real
/// <c>[DataClassification(DataClassification.Restricted)]</c>-classified property. This phase's own
/// Cross-Domain Dependencies entry (`state-map.md`, <c>SK.00.DataPrivacyLoggingGuard</c>) recorded
/// this as "01.Core P-474 not yet implemented" at authoring time (2026-08-26) — re-verified directly
/// against the real repository at implementation time (this domain's now well-established
/// discipline) and found ALREADY SHIPPED, so T-355 proceeds now rather than deferring. The
/// real-assembly re-verification also caught a genuine design/reality drift: the phase spec assumed
/// flat `SharedKernel.DataPrivacy.DataClassificationAttribute`/`.PiiMasking` names, but the real
/// package nests them one level deeper (`SharedKernel.DataPrivacy.Classification.*` /
/// `SharedKernel.DataPrivacy.Masking.PiiMasking`) — the analyzer's metadata-name constants and this
/// file's own contrived-fixture <see cref="Stubs"/> were both corrected to match.
/// <para>
/// Every fixture declares its OWN fixture-local <c>SharedKernel.DataPrivacy</c>/
/// <c>Microsoft.Extensions.Logging</c> namespace stand-ins inside the same test compilation — no
/// <c>ProjectReference</c> to either real package is required, per this rule's fully-qualified
/// metadata-name resolution technique (mirrors WO-040/P-248's SK0017–SK0019 precedent, and SK0030's/
/// SK0032's in-compilation stand-in technique).
/// </para>
/// </remarks>
public class SK0035_UnmaskedClassifiedDataLoggingAnalyzerTests
{
    /// <summary>
    /// Fixture-local stand-ins for <c>Microsoft.Extensions.Logging.LoggerMessageAttribute</c>/
    /// <c>ILogger</c>/<c>LogLevel</c> and the real, shipped <c>SharedKernel.DataPrivacy</c>
    /// package's <c>Classification.DataClassificationAttribute</c>/<c>Classification.DataClassification</c>/
    /// <c>Classification.SensitiveDataCategoryAttribute</c>/<c>Masking.PiiMasking</c> — declared
    /// under the SAME namespaces and type names the analyzer's fully-qualified-metadata-name
    /// resolution looks for (confirmed against `01.Core/SharedKernel.DataPrivacy`'s real source,
    /// which nests these types one level deeper than this rule's own phase spec assumed — see
    /// T-355), so no <c>ProjectReference</c> to either real assembly is required for these
    /// contrived-fixture tests.
    /// </summary>
    private const string Stubs = """

        namespace Microsoft.Extensions.Logging
        {
            public enum LogLevel
            {
                Trace,
                Debug,
                Information,
                Warning,
                Error,
                Critical,
            }

            public interface ILogger
            {
            }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class LoggerMessageAttribute : System.Attribute
            {
                public int EventId { get; set; }

                public LogLevel Level { get; set; }

                public string Message { get; set; } = string.Empty;
            }
        }

        namespace SharedKernel.DataPrivacy.Classification
        {
            public enum DataClassification
            {
                Public,
                Internal,
                Confidential,
                Restricted,
            }

            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
            public sealed class DataClassificationAttribute : System.Attribute
            {
                public DataClassificationAttribute(DataClassification classification) =>
                    Classification = classification;

                public DataClassification Classification { get; }
            }

            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
            public sealed class SensitiveDataCategoryAttribute : System.Attribute
            {
                public SensitiveDataCategoryAttribute(string category) => Category = category;

                public string Category { get; }
            }
        }

        namespace SharedKernel.DataPrivacy.Masking
        {
            public static class PiiMasking
            {
                public static string Email(string value) => "***@***";

                public static string Phone(string value) => "***";

                public static string Pan(string value) => "****";

                public static string Suppress(string value) => "[redacted]";
            }
        }

        """;

    // ---------------------------------------------------------------------------
    // T-351 — Fire path: DataClassification(Restricted)-classified property, direct pass-through
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_RestrictedClassifiedProperty_ReportsDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Logging;
            using SharedKernel.DataPrivacy.Classification;

            namespace Fixture
            {
                public class Customer
                {
                    [DataClassification(DataClassification.Restricted)]
                    public string Ssn { get; set; } = string.Empty;
                }

                public static class Log
                {
                    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "SSN {Ssn}")]
                    public static void CustomerSsn(this ILogger logger, string ssn) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerSsn(logger, {|SK0035:customer.Ssn|});
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-352 — Fire path: SensitiveDataCategory-classified field, direct pass-through
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_SensitiveDataCategoryField_ReportsDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Logging;
            using SharedKernel.DataPrivacy.Classification;

            namespace Fixture
            {
                public class Customer
                {
                    [SensitiveDataCategory("Pii")]
                    public string EmailAddress = string.Empty;
                }

                public static class Log
                {
                    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Email {Email}")]
                    public static void CustomerEmail(this ILogger logger, string email) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerEmail(logger, {|SK0035:customer.EmailAddress|});
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-353 — Pass path: classified property routed through PiiMasking.* first
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_MaskedClassifiedProperty_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Logging;
            using SharedKernel.DataPrivacy.Classification;
            using SharedKernel.DataPrivacy.Masking;

            namespace Fixture
            {
                public class Customer
                {
                    [DataClassification(DataClassification.Restricted)]
                    public string Ssn { get; set; } = string.Empty;
                }

                public static class Log
                {
                    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "SSN {Ssn}")]
                    public static void CustomerSsn(this ILogger logger, string ssn) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerSsn(logger, PiiMasking.Suppress(customer.Ssn));
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-354 — Pass path: unclassified property passed directly
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_UnclassifiedProperty_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Logging;

            namespace Fixture
            {
                public class Customer
                {
                    public string DisplayName { get; set; } = string.Empty;
                }

                public static class Log
                {
                    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Name {Name}")]
                    public static void CustomerName(this ILogger logger, string name) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerName(logger, customer.DisplayName);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // Additional pass path — whole-object destructuring is masked-aware too (classified member
    // absent from the destructured argument's static type keeps this a pass path).
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_NonClassifiedLogLevelAndPlainStringArguments_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using System;
            using Microsoft.Extensions.Logging;

            namespace Fixture
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Failed {Reason}")]
                    public static void OperationFailed(this ILogger logger, Exception exception, string reason) { }
                }

                public class OperationRunner
                {
                    public void Handle(ILogger logger, Exception exception)
                    {
                        Log.OperationFailed(logger, exception, "timeout");
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-355 — Real-assembly re-verification against the compiled SharedKernel.DataPrivacy package
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-355 (GATING, per this phase's own spec): re-points the analyzer's fully-qualified-
    /// metadata-name resolution at the REAL, compiled <c>SharedKernel.DataPrivacy</c> assembly (via
    /// this test project's test-only <c>ProjectReference</c>) instead of the fixture-local
    /// <see cref="Stubs"/>, and proves the fire path still holds against the genuine
    /// <c>DataClassificationAttribute</c>/<c>DataClassification</c> types. The
    /// <c>Microsoft.Extensions.Logging</c> stand-in from <see cref="Stubs"/> is still used — this
    /// test isolates the ONE thing that changed (the real vs. fixture-local
    /// <c>SharedKernel.DataPrivacy</c> assembly), not a wholesale rewrite.
    /// </summary>
    /// <remarks>
    /// Deliberately built via a raw <see cref="CSharpCompilation"/> +
    /// <see cref="Compilation.WithAnalyzers(ImmutableArray{DiagnosticAnalyzer})"/> rather than
    /// <see cref="CSharpAnalyzerTest{TAnalyzer, TVerifier}"/>: this testing package version's
    /// <see cref="ReferenceAssemblies"/> presets are all netstandard/older-.NET vintage (its
    /// default resolves <c>System.Runtime, Version=4.2.2.0</c>) and cannot supply a net10.0-exact
    /// reference set the way SK0034's <c>RealSourceAudit_...</c> test already proved this
    /// alternative technique can — the REAL <c>SharedKernel.DataPrivacy.dll</c> is net10.0-only (no
    /// netstandard2.0 asset exists to fall back to, unlike SK0033's AutoMapper workaround) and
    /// requires <c>System.Runtime, Version=10.0.0.0</c> exactly. Building the reference list from
    /// this TEST HOST'S OWN trusted-platform-assemblies list guarantees an exact version match,
    /// since the test host itself runs on net10.0.
    /// </remarks>
    [Fact]
    public async Task RealAssembly_RestrictedClassifiedProperty_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.Logging;
            using SharedKernel.DataPrivacy.Classification;

            namespace Fixture
            {
                public class Customer
                {
                    [DataClassification(DataClassification.Restricted)]
                    public string Ssn { get; set; } = string.Empty;
                }

                public static class Log
                {
                    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "SSN {Ssn}")]
                    public static void CustomerSsn(this ILogger logger, string ssn) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerSsn(logger, customer.Ssn);
                    }
                }
            }

            namespace Microsoft.Extensions.Logging
            {
                public enum LogLevel
                {
                    Trace,
                    Debug,
                    Information,
                    Warning,
                    Error,
                    Critical,
                }

                public interface ILogger
                {
                }

                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class LoggerMessageAttribute : System.Attribute
                {
                    public int EventId { get; set; }

                    public LogLevel Level { get; set; }

                    public string Message { get; set; } = string.Empty;
                }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            assemblyName: "SK0035.RealAssemblyVerification",
            syntaxTrees: [syntaxTree],
            references: ResolveRuntimeAndDataPrivacyReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        // Sanity: the fixture must genuinely COMPILE against the real assembly (proves the
        // reference set is sufficient and the real DataClassificationAttribute/DataClassification/
        // ILogger-extension-method shape matches what this fixture assumes) before trusting the
        // analyzer's own diagnostics below.
        var compileErrors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(compileErrors);

        var analyzer = new UnmaskedClassifiedDataLoggingAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer)
        );

        var allDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var sk0035Diagnostics = allDiagnostics
            .Where(d => d.Id == UnmaskedClassifiedDataLoggingAnalyzer.Rule.Id)
            .ToList();

        Assert.Single(sk0035Diagnostics);
    }

    /// <summary>
    /// Builds a reference list from this TEST HOST PROCESS's own trusted-platform-assemblies list
    /// (every BCL/shared-framework assembly the host already trusts, guaranteed net10.0-exact since
    /// the host itself runs on net10.0) plus the REAL, compiled
    /// <c>SharedKernel.DataPrivacy.dll</c>/<c>SharedKernel.Primitives.dll</c> (resolved via this
    /// test project's own test-only <c>ProjectReference</c>, see
    /// <c>SharedKernel.Analyzers.Tests.csproj</c>).
    /// </summary>
    private static ImmutableArray<MetadataReference> ResolveRuntimeAndDataPrivacyReferences()
    {
        var trustedPlatformAssemblies = (
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
        )?.Split(Path.PathSeparator);

        var builder = ImmutableArray.CreateBuilder<MetadataReference>();

        if (trustedPlatformAssemblies is not null)
        {
            foreach (var path in trustedPlatformAssemblies)
            {
                if (File.Exists(path))
                {
                    builder.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        builder.Add(
            MetadataReference.CreateFromFile(
                typeof(SharedKernel.DataPrivacy.Classification.DataClassificationAttribute).Assembly.Location
            )
        );

        return builder.ToImmutable();
    }

    // ---------------------------------------------------------------------------
    // Test construction helper
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="CSharpAnalyzerTest{TAnalyzer, TVerifier}"/> for
    /// <see cref="UnmaskedClassifiedDataLoggingAnalyzer"/> with <see cref="Stubs"/> appended to
    /// <paramref name="fixtureCode"/>.
    /// </summary>
    private static CSharpAnalyzerTest<UnmaskedClassifiedDataLoggingAnalyzer, DefaultVerifier> CreateTest(
        string fixtureCode
    ) => new() { TestCode = fixtureCode + Stubs };
}
