using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0036 <see cref="RawRpcExceptionConstructionAnalyzer"/>.</summary>
/// <remarks>
/// T-356: Fire path — <c>new RpcException(new Status(...))</c> outside
/// <c>SharedKernel.Presentation.Grpc</c>.
/// T-357: Fire path — a bare, not-immediately-wrapped <c>new Status(...)</c> outside the exempt
/// namespace.
/// T-358: Pass path — the identical construction inside a fixture-local
/// <c>SharedKernel.Presentation.Grpc</c> namespace or one of its sub-namespaces (exemption proof).
/// T-359: Pass path — a plain sanctioned-extension-shaped method invocation, no direct
/// construction.
/// <para>
/// The fixtures follow the P-562 gRPC layout — <c>GrpcResultExtensions</c> in the package's root namespace with
/// <c>ThrowIfFailure()</c>/<c>GetValueOrThrow()</c> (formerly <c>…Grpc.Results</c> and <c>ToGrpcResult()</c>) and the
/// rich-status factory in <c>…Grpc.Errors</c>. The rule itself is unchanged.
/// </para>
/// <para>
/// Every test references the REAL <c>Grpc.Core.Api</c> package (via this test project's own
/// compile-time <c>PackageReference</c>) — SK0036's own design requires exact type resolution to
/// <c>Grpc.Core.RpcException</c>/<c>Grpc.Core.Status</c>, which only the real types can prove.
/// </para>
/// </remarks>
public class SK0036_RawRpcExceptionConstructionAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-356 — Fire path: new RpcException(new Status(...))
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-356: <c>new RpcException(new Status(...))</c> is a NESTED construction — both the outer
    /// <c>RpcException</c> and the inner <c>Status</c> are independently flagged (Implementation
    /// Rule 2: two constructed-type checks under one diagnostic ID), so this fixture expects two
    /// SK0036 diagnostics, one per constructor call.
    /// </summary>
    [Fact]
    public async Task FirePath_RpcExceptionWrappingStatus_ReportsDiagnostic()
    {
        var test = CreateTest(
            """
            using Grpc.Core;

            namespace Fixture
            {
                public class OrderService
                {
                    public void Handle()
                    {
                        throw {|SK0036:new RpcException({|SK0036:new Status(StatusCode.NotFound, "not found")|})|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-357 — Fire path: bare new Status(...), not immediately wrapped
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_BareStatusConstruction_ReportsDiagnostic()
    {
        var test = CreateTest(
            """
            using Grpc.Core;

            namespace Fixture
            {
                public class OrderService
                {
                    public Status BuildStatus() =>
                        {|SK0036:new Status(StatusCode.InvalidArgument, "bad request")|};
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-358 — Pass path: identical construction inside the exempt namespace
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_InsideSharedKernelPresentationGrpcNamespace_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Grpc.Core;

            namespace SharedKernel.Presentation.Grpc
            {
                public static class GrpcResultExtensions
                {
                    public static void ThrowIfFailure()
                    {
                        throw new RpcException(new Status(StatusCode.NotFound, "not found"));
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_InsideASharedKernelPresentationGrpcSubNamespace_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Grpc.Core;

            namespace SharedKernel.Presentation.Grpc.Errors
            {
                internal static class RpcStatusFactory
                {
                    public static RpcException CreateException(StatusCode code, string message) =>
                        new RpcException(new Status(code, message));
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-359 — Pass path: plain sanctioned-extension-shaped method invocation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_SanctionedExtensionInvocation_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Grpc.Core;

            namespace Fixture
            {
                public static class GrpcResultExtensions
                {
                    public static void ThrowIfFailure(object result)
                    {
                    }

                    public static T GetValueOrThrow<T>(T result) => result;
                }

                public class OrderService
                {
                    public int Handle(object result, int value)
                    {
                        GrpcResultExtensions.ThrowIfFailure(result);
                        return GrpcResultExtensions.GetValueOrThrow(value);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // The message points at the mapping that exists (P-562)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The diagnostic tells the developer what to call instead, so it must name the current gRPC result mapping —
    /// <c>ThrowIfFailure()</c>/<c>GetValueOrThrow()</c> in the package's root namespace — and never the deleted
    /// <c>ToGrpcResult()</c> or its former <c>…Grpc.Results</c> namespace.
    /// </summary>
    [Fact]
    public void Message_NamesTheCurrentResultMapping()
    {
        var message = RawRpcExceptionConstructionAnalyzer.Rule.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains("SharedKernel.Presentation.Grpc.GrpcResultExtensions.ThrowIfFailure()", message, StringComparison.Ordinal);
        Assert.Contains("GetValueOrThrow()", message, StringComparison.Ordinal);
        Assert.DoesNotContain("ToGrpcResult", message, StringComparison.Ordinal);
        Assert.DoesNotContain(".Results.", message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // Test construction helper
    // ---------------------------------------------------------------------------

    private static CSharpAnalyzerTest<RawRpcExceptionConstructionAnalyzer, DefaultVerifier> CreateTest(
        string source
    )
    {
        var test = new CSharpAnalyzerTest<RawRpcExceptionConstructionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };

        // The real, lightweight Grpc.Core.Api package (RpcException/Status/StatusCode only) — this
        // rule's own design requires exact type resolution to Grpc.Core.RpcException/Grpc.Core.Status,
        // which only the real types can prove. No net10.0-vs-netstandard2.0 version conflict exists
        // here (unlike SK0033's AutoMapper/SK0035's SharedKernel.DataPrivacy): Grpc.Core.Api 2.80.0
        // ships no net10.0-specific asset at all, so NuGet already resolves this test project's own
        // reference to the netstandard2.0/2.1 asset — the same one `typeof(...).Assembly.Location`
        // returns here.
        test.TestState.AdditionalReferences.Add(
            MetadataReference.CreateFromFile(typeof(Grpc.Core.RpcException).Assembly.Location)
        );

        return test;
    }
}
