using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0002 DirectMicrosoftFeatureManagerAnalyzer.</summary>
public class SK0002_DirectMicrosoftFeatureManagerAnalyzerTests
{
    [Fact]
    public async Task FirePath_ConstructorParameter_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectMicrosoftFeatureManagerAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace Microsoft.FeatureManagement
                {
                    public interface IFeatureManager { }
                }

                namespace MyApp
                {
                    using Microsoft.FeatureManagement;

                    public class MyService
                    {
                        public MyService({|SK0002:IFeatureManager|} featureManager) { }
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
            TestCode = """
                namespace Microsoft.FeatureManagement
                {
                    public interface IFeatureManager { }
                }

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
    public async Task PassPath_SharedKernelFeatureManager_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectMicrosoftFeatureManagerAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SharedKernel.FeatureManagement
                {
                    public interface IFeatureManager { }
                }

                namespace MyApp
                {
                    using SharedKernel.FeatureManagement;

                    public class MyService
                    {
                        public MyService(IFeatureManager featureManager) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}
