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
/// T-359: Pass path — the sanctioned path, no direct construction.
/// <para>
/// The fixtures follow the gRPC layout after the P-562 final review (R32): the package constructs statuses only in
/// its rich-status factory (<c>…Grpc.Errors</c>) and its exception interceptor (<c>…Grpc.Interceptors</c>), and a
/// service ends a failed <c>Result</c> with <c>SharedKernel.Core.Extensions</c>' <c>ThrowIfFailure()</c>/
/// <c>GetValueOrThrow()</c>. The package's own extensions of those names (and before them <c>ToGrpcResult()</c>) are
/// gone. The rule itself is unchanged.
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

    /// <summary>
    /// T-358: the prefix itself exempts the package's root namespace. Since R32 no shipped root-namespace type
    /// constructs a status, so the fixture type is neutral.
    /// </summary>
    [Fact]
    public async Task PassPath_InsideSharedKernelPresentationGrpcNamespace_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Grpc.Core;

            namespace SharedKernel.Presentation.Grpc
            {
                internal static class RootNamespaceStatusBuilder
                {
                    public static RpcException NotFound() =>
                        new RpcException(new Status(StatusCode.NotFound, "not found"));
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
    // T-359 — Pass path: the sanctioned path, SharedKernel.Core's result extensions
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-359: a service that ends failed results with <c>SharedKernel.Core.Extensions</c>' <c>GetValueOrThrow()</c>/
    /// <c>ThrowIfFailure()</c> constructs no status and is not flagged. The stubs keep Core's signatures and, like
    /// Core, throw the error's exception rather than an <c>RpcException</c>.
    /// </summary>
    [Fact]
    public async Task PassPath_CoreResultExtensions_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using System;
            using System.Threading.Tasks;
            using Grpc.Core;
            using SharedKernel.Core.Extensions;
            using SharedKernel.Primitives.Results;

            namespace SharedKernel.Primitives.Results
            {
                public readonly struct Result
                {
                    public bool IsFailure { get; }
                }

                public sealed class Result<T>
                {
                    public bool IsFailure { get; set; }
                    public T Value { get; set; }
                }
            }

            namespace SharedKernel.Core.Extensions
            {
                public static class ResultExtensions
                {
                    public static void ThrowIfFailure(this Result result)
                    {
                        if (result.IsFailure)
                            throw new InvalidOperationException("The error's exception.");
                    }

                    public static T GetValueOrThrow<T>(this Result<T> result) =>
                        result.IsFailure ? throw new InvalidOperationException("The error's exception.") : result.Value;
                }
            }

            namespace Fixture
            {
                public sealed class OrdersService
                {
                    public Task<int> GetOrder(Result<int> found, ServerCallContext context) =>
                        Task.FromResult(found.GetValueOrThrow());

                    public Task CancelOrder(Result cancelled, ServerCallContext context)
                    {
                        cancelled.ThrowIfFailure();
                        return Task.CompletedTask;
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // The message points at the path that exists (P-562 R32)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The diagnostic tells the developer what to call instead, so it must name <c>SharedKernel.Core</c>'s
    /// <c>ThrowIfFailure()</c>/<c>GetValueOrThrow()</c> — never the gRPC package's <c>GrpcResultExtensions</c>, which
    /// R32 removed, nor its earlier <c>ToGrpcResult()</c> in <c>…Grpc.Results</c>. The expected names come from the
    /// compiled type, so a rename in <c>SharedKernel.Core</c> breaks this test instead of leaving the message stale.
    /// </summary>
    [Fact]
    public void Message_NamesTheCurrentResultMapping()
    {
        var message = RawRpcExceptionConstructionAnalyzer.Rule.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains(typeof(SharedKernel.Core.Extensions.ResultExtensions).Namespace!, message, StringComparison.Ordinal);
        Assert.Contains($"{nameof(SharedKernel.Core.Extensions.ResultExtensions.ThrowIfFailure)}()", message, StringComparison.Ordinal);
        Assert.Contains($"{nameof(SharedKernel.Core.Extensions.ResultExtensions.GetValueOrThrow)}()", message, StringComparison.Ordinal);
        Assert.DoesNotContain("GrpcResultExtensions", message, StringComparison.Ordinal);
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
