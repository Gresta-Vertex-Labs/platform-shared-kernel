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
/// Fixtures declare stand-ins for the Microsoft compliance/logging types and the
/// <c>SharedKernel.DataPrivacy</c> masking types under their real fully-qualified names, so no
/// package reference is needed (the analyzer resolves everything by metadata name). One test,
/// <see cref="RealAssembly_ClassifiedProperty_ReportsDiagnostic"/>, runs against the compiled
/// <c>SharedKernel.DataPrivacy</c> assembly to lock the metadata names to the real types (P-554).
/// </remarks>
public class SK0035_UnmaskedClassifiedDataLoggingAnalyzerTests
{
    private const string LoggingStubs = """

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

            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class LogPropertiesAttribute : System.Attribute
            {
            }
        }

        """;

    private const string ComplianceStubs = """

        namespace Microsoft.Extensions.Compliance.Classification
        {
            public abstract class DataClassificationAttribute : System.Attribute
            {
            }

            public sealed class NoDataClassificationAttribute : DataClassificationAttribute
            {
            }
        }

        namespace SharedKernel.DataPrivacy.Classification
        {
            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field | System.AttributeTargets.Parameter)]
            public sealed class EmailAddressDataAttribute
                : Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute
            {
            }
        }

        namespace SharedKernel.DataPrivacy.Masking
        {
            public static class PiiMasking
            {
                public static string Email(string value) => "***@***";

                public static string Suppress(string value) => "[redacted]";
            }

            public sealed class Pseudonymizer
            {
                public string Pseudonymize(string value) => "token";
            }
        }

        """;

    private const string Stubs = LoggingStubs + ComplianceStubs;

    private const string CustomerType = """

        namespace Fixture
        {
            using Microsoft.Extensions.Compliance.Classification;
            using SharedKernel.DataPrivacy.Classification;

            public class Customer
            {
                [EmailAddressData]
                public string Email { get; set; } = string.Empty;

                [NoDataClassification]
                public string Plan { get; set; } = string.Empty;

                public string UserId { get; set; } = string.Empty;
            }
        }

        """;

    [Fact]
    public async Task DirectClassifiedMember_ToUnclassifiedParameter_ReportsDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Logging;

            namespace Fixture
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Email {Email}")]
                    public static void CustomerEmail(this ILogger logger, string email) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerEmail(logger, {|#0:customer.Email|});
                    }
                }
            }
            """
        );
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(UnmaskedClassifiedDataLoggingAnalyzer.Rule)
                .WithLocation(0)
                .WithArguments("Customer.Email", "[EmailAddressData]", "email")
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task DirectClassifiedMember_ToClassifiedParameter_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Logging;
            using SharedKernel.DataPrivacy.Classification;

            namespace Fixture
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Email {Email}")]
                    public static void CustomerEmail(this ILogger logger, [EmailAddressData] string email) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerEmail(logger, customer.Email);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task DirectClassifiedMember_ToLogPropertiesParameter_ReportsDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Logging;

            namespace Fixture
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Email {Email}")]
                    public static void CustomerEmail(this ILogger logger, [LogProperties] string email) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerEmail(logger, {|SK0035:customer.Email|});
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task ObjectWithClassifiedMember_ToUnclassifiedParameter_ReportsDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Logging;

            namespace Fixture
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Customer {Customer}")]
                    public static void CustomerSeen(this ILogger logger, Customer customer) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerSeen(logger, {|SK0035:customer|});
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task ObjectWithClassifiedMember_ToLogPropertiesParameter_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Logging;

            namespace Fixture
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Customer seen")]
                    public static void CustomerSeen(this ILogger logger, [LogProperties] Customer customer) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerSeen(logger, customer);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task NoDataClassificationMember_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Compliance.Classification;
            using Microsoft.Extensions.Logging;

            namespace Fixture
            {
                public class Subscription
                {
                    [NoDataClassification]
                    public string Plan { get; set; } = string.Empty;
                }

                public static class Log
                {
                    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Plan {Plan}")]
                    public static void PlanChosen(this ILogger logger, string plan) { }

                    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Subscription {Subscription}")]
                    public static void SubscriptionSeen(this ILogger logger, Subscription subscription) { }
                }

                public class SubscriptionService
                {
                    public void Handle(ILogger logger, Customer customer, Subscription subscription)
                    {
                        Log.PlanChosen(logger, customer.Plan);
                        Log.SubscriptionSeen(logger, subscription);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task PiiMaskingWrappedMember_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Logging;
            using SharedKernel.DataPrivacy.Masking;

            namespace Fixture
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Email {Email}")]
                    public static void CustomerEmail(this ILogger logger, string email) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerEmail(logger, PiiMasking.Email(customer.Email));
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task PseudonymizedMember_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Logging;
            using SharedKernel.DataPrivacy.Masking;

            namespace Fixture
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Email {Email}")]
                    public static void CustomerEmail(this ILogger logger, string email) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer, Pseudonymizer pseudonymizer)
                    {
                        Log.CustomerEmail(logger, pseudonymizer.Pseudonymize(customer.Email));
                        Log.CustomerEmail(logger, pseudonymizer.Pseudonymize(customer.UserId));
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task IndirectlyDerivedClassificationAttribute_ReportsDiagnostic()
    {
        var test = CreateTest(
            """
            using Microsoft.Extensions.Compliance.Classification;
            using Microsoft.Extensions.Logging;

            namespace Fixture
            {
                public abstract class HealthClassificationAttribute : DataClassificationAttribute
                {
                }

                [System.AttributeUsage(System.AttributeTargets.Property)]
                public sealed class DiagnosisDataAttribute : HealthClassificationAttribute
                {
                }

                public class Patient
                {
                    [DiagnosisData]
                    public string Diagnosis { get; set; } = string.Empty;
                }

                public static class Log
                {
                    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Diagnosis {Diagnosis}")]
                    public static void Diagnosed(this ILogger logger, string diagnosis) { }
                }

                public class PatientService
                {
                    public void Handle(ILogger logger, Patient patient)
                    {
                        Log.Diagnosed(logger, {|#0:patient.Diagnosis|});
                    }
                }
            }
            """
        );
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(UnmaskedClassifiedDataLoggingAnalyzer.Rule)
                .WithLocation(0)
                .WithArguments("Patient.Diagnosis", "[DiagnosisData]", "diagnosis")
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task ExceptionAndLogLevelParameters_AreIgnored()
    {
        var test = CreateTest(
            """
            using System;
            using Microsoft.Extensions.Compliance.Classification;
            using Microsoft.Extensions.Logging;

            namespace Fixture
            {
                public sealed class CustomerException : Exception
                {
                    [NoDataClassification]
                    public string Code { get; set; } = string.Empty;

                    [SharedKernel.DataPrivacy.Classification.EmailAddressData]
                    public string Email { get; set; } = string.Empty;
                }

                public static class Log
                {
                    [LoggerMessage(EventId = 6, Message = "Failed {Reason}")]
                    public static void OperationFailed(this ILogger logger, LogLevel level, CustomerException exception, string reason) { }
                }

                public class OperationRunner
                {
                    public void Handle(ILogger logger, CustomerException exception)
                    {
                        Log.OperationFailed(logger, LogLevel.Error, exception, "timeout");
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task CompilationWithoutComplianceBaseType_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<UnmaskedClassifiedDataLoggingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using Microsoft.Extensions.Logging;

                namespace Fixture
                {
                    [System.AttributeUsage(System.AttributeTargets.Property)]
                    public sealed class DataClassificationAttribute : System.Attribute
                    {
                    }

                    public class Customer
                    {
                        [DataClassification]
                        public string Email { get; set; } = string.Empty;
                    }

                    public static class Log
                    {
                        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Email {Email}")]
                        public static void CustomerEmail(this ILogger logger, string email) { }
                    }

                    public class CustomerService
                    {
                        public void Handle(ILogger logger, Customer customer)
                        {
                            Log.CustomerEmail(logger, customer.Email);
                        }
                    }
                }
                """ + LoggingStubs,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Runs the analyzer against the compiled <c>SharedKernel.DataPrivacy</c> assembly (and the real
    /// <c>Microsoft.Extensions.Compliance.Abstractions</c> it builds on), so the metadata names the
    /// analyzer resolves cannot drift from the shipped types.
    /// </summary>
    /// <remarks>
    /// Built on a raw <see cref="CSharpCompilation"/> because the testing package's
    /// <see cref="ReferenceAssemblies"/> presets cannot supply net10.0 references; the host's own
    /// trusted-platform-assemblies list gives an exact match.
    /// </remarks>
    [Fact]
    public async Task RealAssembly_ClassifiedProperty_ReportsDiagnostic()
    {
        const string source = """
            using SharedKernel.DataPrivacy.Classification;
            using SharedKernel.DataPrivacy.Masking;
            using Microsoft.Extensions.Logging;

            namespace Fixture
            {
                public class Customer
                {
                    [EmailAddressData]
                    public string Email { get; set; } = string.Empty;
                }

                public static class Log
                {
                    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Email {Email}")]
                    public static void CustomerEmail(this ILogger logger, string email) { }

                    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Email {Email}")]
                    public static void RedactedEmail(this ILogger logger, [EmailAddressData] string email) { }
                }

                public class CustomerService
                {
                    public void Handle(ILogger logger, Customer customer)
                    {
                        Log.CustomerEmail(logger, customer.Email);
                        Log.CustomerEmail(logger, PiiMasking.Email(customer.Email));
                        Log.RedactedEmail(logger, customer.Email);
                    }
                }
            }
            """ + LoggingStubs;

        var compilation = CSharpCompilation.Create(
            assemblyName: "SK0035.RealAssemblyVerification",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: ResolveRuntimeAndDataPrivacyReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var compileErrors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(compileErrors);

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new UnmaskedClassifiedDataLoggingAnalyzer())
        );

        var diagnostics = (await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync())
            .Where(d => d.Id == UnmaskedClassifiedDataLoggingAnalyzer.Rule.Id)
            .ToList();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("[EmailAddressData]", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static ImmutableArray<MetadataReference> ResolveRuntimeAndDataPrivacyReferences()
    {
        var paths = (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [])
            .Append(typeof(SharedKernel.DataPrivacy.Classification.EmailAddressDataAttribute).Assembly.Location)
            .Append(
                typeof(Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute).Assembly.Location
            )
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return [.. paths.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))];
    }

    /// <summary>
    /// Builds a test for <paramref name="fixtureCode"/> with the shared <c>Customer</c> type and all
    /// stubs appended.
    /// </summary>
    private static CSharpAnalyzerTest<UnmaskedClassifiedDataLoggingAnalyzer, DefaultVerifier> CreateTest(
        string fixtureCode
    ) => new() { TestCode = fixtureCode + "\n" + CustomerType + Stubs };
}
