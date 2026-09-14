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
                namespace SharedKernel.FeatureManagement.Abstractions
                {
                    public interface IFeatureManager { }
                }

                namespace MyApp
                {
                    using SharedKernel.FeatureManagement.Abstractions;

                    public class MyService
                    {
                        public MyService(IFeatureManager featureManager) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// The diagnostic tells the developer which type to use instead, so that name must be the real
    /// one. The message once named <c>SharedKernel.FeatureManagement.IFeatureManager</c>, a type
    /// that does not exist; the shipped interface lives in the <c>.Abstractions</c> namespace.
    /// </summary>
    [Fact]
    public void Message_NamesTheShippedSharedKernelAbstraction()
    {
        var shippedName = typeof(SharedKernel.FeatureManagement.Abstractions.IFeatureManager).FullName;

        Assert.Contains(
            $"'{shippedName}'",
            DirectMicrosoftFeatureManagerAnalyzer.Rule.MessageFormat.ToString(),
            StringComparison.Ordinal
        );
    }
}
