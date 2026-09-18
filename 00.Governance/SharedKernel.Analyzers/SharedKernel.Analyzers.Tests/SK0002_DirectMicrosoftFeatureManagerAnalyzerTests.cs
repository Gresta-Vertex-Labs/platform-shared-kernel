using System;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for SK0002 DirectMicrosoftFeatureManagerAnalyzer — redesigned for P-555, the
/// <c>SharedKernel.FeatureManagement</c> OpenFeature migration. Covers all four forbidden Microsoft
/// evaluator interfaces, the <c>OpenFeature.Api.Instance</c> ambient-API shape, the
/// <c>OpenFeature.IFeatureClient</c> pass path, and the owning-package (<c>SharedKernel.FeatureManagement</c>)
/// exemption.
/// </summary>
/// <remarks>
/// Every test declares its own stub types for <c>Microsoft.FeatureManagement</c> and
/// <c>OpenFeature</c> — the real packages are not referenced by <c>SharedKernel.Analyzers.Tests</c>,
/// mirroring how the original SK0002 tests stubbed <c>Microsoft.FeatureManagement.IFeatureManager</c>.
/// </remarks>
public class SK0002_DirectMicrosoftFeatureManagerAnalyzerTests
{
    private const string MicrosoftFeatureManagementStubs = """
        namespace Microsoft.FeatureManagement
        {
            public interface IFeatureManager { }
            public interface IVariantFeatureManager { }
            public interface IFeatureManagerSnapshot { }
            public interface IVariantFeatureManagerSnapshot { }
        }
        """;

    private const string OpenFeatureStubs = """
        namespace OpenFeature
        {
            public interface IFeatureClient { }

            public sealed class Api
            {
                public static Api Instance { get; } = new Api();

                public IFeatureClient GetClient() => null!;
            }
        }
        """;

    [Theory]
    [InlineData("IFeatureManager")]
    [InlineData("IVariantFeatureManager")]
    [InlineData("IFeatureManagerSnapshot")]
    [InlineData("IVariantFeatureManagerSnapshot")]
    public async Task FirePath_ConstructorParameter_ReportsDiagnosticForEachForbiddenInterface(
        string interfaceName
    )
    {
        var test = new CSharpAnalyzerTest<DirectMicrosoftFeatureManagerAnalyzer, DefaultVerifier>
        {
            TestCode =
                MicrosoftFeatureManagementStubs
                + $$"""

                namespace MyApp
                {
                    using Microsoft.FeatureManagement;

                    public class MyService
                    {
                        public MyService({|SK0002:{{interfaceName}}|} featureManager) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_FieldDeclaration_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectMicrosoftFeatureManagerAnalyzer, DefaultVerifier>
        {
            TestCode =
                MicrosoftFeatureManagementStubs
                + """

                namespace MyApp
                {
                    using Microsoft.FeatureManagement;

                    public class MyService
                    {
                        private {|SK0002:IFeatureManager|} _featureManager = null!;
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_PropertyDeclaration_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectMicrosoftFeatureManagerAnalyzer, DefaultVerifier>
        {
            TestCode =
                MicrosoftFeatureManagementStubs
                + """

                namespace MyApp
                {
                    using Microsoft.FeatureManagement;

                    public class MyService
                    {
                        public {|SK0002:IVariantFeatureManager|} Manager { get; set; } = null!;
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_OpenFeatureApiInstance_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectMicrosoftFeatureManagerAnalyzer, DefaultVerifier>
        {
            TestCode =
                OpenFeatureStubs
                + """

                namespace MyApp
                {
                    using OpenFeature;

                    public class MyService
                    {
                        public IFeatureClient GetClient() => {|SK0002:Api.Instance|}.GetClient();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_OpenFeatureApiInstance_FullyQualified_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectMicrosoftFeatureManagerAnalyzer, DefaultVerifier>
        {
            TestCode =
                OpenFeatureStubs
                + """

                namespace MyApp
                {
                    public class MyService
                    {
                        public OpenFeature.IFeatureClient GetClient() =>
                            {|SK0002:OpenFeature.Api.Instance|}.GetClient();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_OpenFeatureIFeatureClient_ConstructorParameter_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectMicrosoftFeatureManagerAnalyzer, DefaultVerifier>
        {
            TestCode =
                OpenFeatureStubs
                + """

                namespace MyApp
                {
                    using OpenFeature;

                    public class MyService
                    {
                        public MyService(IFeatureClient client) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_UnrelatedApiInstanceMember_NoDiagnostic()
    {
        // A same-simple-named "Instance" property on an unrelated "Api" type must never
        // false-positive — only the real OpenFeature.Api.Instance property symbol is flagged.
        var test = new CSharpAnalyzerTest<DirectMicrosoftFeatureManagerAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace MyApp
                {
                    public sealed class Api
                    {
                        public static Api Instance { get; } = new Api();
                    }

                    public class MyService
                    {
                        public Api Get() => Api.Instance;
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_OwningPackageAssembly_IVariantFeatureManagerBridge_NoDiagnostic()
    {
        // SharedKernel.FeatureManagement's own internal OpenFeature provider adapter legitimately
        // implements against Microsoft.FeatureManagement.IVariantFeatureManager to bridge it into
        // OpenFeature — the compilation's assembly name itself is the exemption key.
        var test = new CSharpAnalyzerTest<DirectMicrosoftFeatureManagerAnalyzer, DefaultVerifier>
        {
            TestCode =
                MicrosoftFeatureManagementStubs
                + """

                namespace SharedKernel.FeatureManagement.Internal
                {
                    using Microsoft.FeatureManagement;

                    internal sealed class MicrosoftFeatureManagementProvider
                    {
                        public MicrosoftFeatureManagementProvider(IVariantFeatureManager manager) { }
                    }
                }
                """,
        };

        // The exemption is keyed on the compiling assembly's name, so the test project itself must
        // be named "SharedKernel.FeatureManagement" for this assertion to be meaningful.
        test.SolutionTransforms.Add(
            (solution, projectId) => solution.WithProjectAssemblyName(projectId, "SharedKernel.FeatureManagement")
        );

        await test.RunAsync();
    }

    /// <summary>
    /// The diagnostic tells the developer which type to use instead, so that name must be the real
    /// one. The message once named the deleted <c>SharedKernel.FeatureManagement.Abstractions.IFeatureManager</c>
    /// type; the redesigned rule (P-555) points at the CNCF-standard <c>OpenFeature.IFeatureClient</c>
    /// and the typed <c>SharedKernel.FeatureManagement.FeatureFlag&lt;T&gt;</c> declaration instead.
    /// </summary>
    [Fact]
    public void Message_NamesOpenFeatureIFeatureClientAndFeatureFlag()
    {
        var messageFormat = DirectMicrosoftFeatureManagerAnalyzer.Rule.MessageFormat.ToString();

        Assert.Contains("'OpenFeature.IFeatureClient'", messageFormat, StringComparison.Ordinal);
        Assert.Contains(
            "'SharedKernel.FeatureManagement.FeatureFlag<T>'",
            messageFormat,
            StringComparison.Ordinal
        );
    }
}
