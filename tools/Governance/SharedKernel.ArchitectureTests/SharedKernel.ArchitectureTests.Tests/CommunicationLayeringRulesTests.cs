using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="CommunicationLayeringRules"/> — the 11.Communication domain boundary
/// enforcement predicates introduced by WO-025 P-159. The two former sibling/layer rules were deleted in P-574
/// (the tier check covers them).
/// </summary>
/// <remarks>
/// T-120/T-121: <see cref="CommunicationLayeringRules.NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc"/>
/// T-122/T-123: <see cref="CommunicationLayeringRules.NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL"/>
/// </remarks>
public class CommunicationLayeringRulesTests
{
    // ---------------------------------------------------------------------------
    // T-120 — Fire path: type inherits Grpc.Core.Interceptors.Interceptor directly
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-120: A contrived assembly containing a class that directly inherits
    /// <c>Grpc.Core.Interceptors.Interceptor</c> and is NOT in
    /// <c>SharedKernel.Communication.Grpc</c> must fail
    /// <see cref="CommunicationLayeringRules.NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc"/>.
    /// </summary>
    [Fact]
    public void NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc_DirectInheritance_RuleFails()
    {
        const string source = """
            namespace Grpc.Core.Interceptors
            {
                // Stub simulating the gRPC Interceptor base class
                public abstract class Interceptor { }
            }

            namespace Application.GrpcHandlers
            {
                using Grpc.Core.Interceptors;

                // Violation: direct inheritance outside SharedKernel.Communication.Grpc
                public class CustomTenantInterceptor : Interceptor { }
            }
            """;

        var assembly = CompileInMemory("Fixture.GrpcInterceptor.Violation", source);

        var result = CommunicationLayeringRules
            .NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "CustomTenantInterceptor inherits from Grpc.Core.Interceptors.Interceptor " +
                     "directly outside SharedKernel.Communication.Grpc — violates the rule");
    }

    // ---------------------------------------------------------------------------
    // T-121 — Pass path: type uses platform wrapper, not direct Interceptor
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-121: A contrived assembly containing a type that inherits from a platform wrapper
    /// (<c>GrpcClientInterceptorBase</c>) rather than <c>Grpc.Core.Interceptors.Interceptor</c>
    /// directly must pass
    /// <see cref="CommunicationLayeringRules.NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc"/>.
    /// </summary>
    [Fact]
    public void NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc_PlatformWrapper_RulePasses()
    {
        const string source = """
            namespace Application.GrpcClients
            {
                // Simulates a platform wrapper (not a direct Interceptor subclass)
                public abstract class GrpcClientInterceptorBase { }

                // Compliant: inherits from platform wrapper, not directly from Interceptor
                public class OrderGrpcInterceptor : GrpcClientInterceptorBase { }
            }
            """;

        var assembly = CompileInMemory("Fixture.GrpcInterceptor.Compliant", source);

        var result = CommunicationLayeringRules
            .NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderGrpcInterceptor inherits from GrpcClientInterceptorBase " +
                     "(not directly from Grpc.Core.Interceptors.Interceptor)");
    }

    // ---------------------------------------------------------------------------
    // T-122 — Fire path: type inherits FilterInputType<T> without FilterBase<T>
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-122: A contrived assembly containing a class that directly inherits
    /// <c>FilterInputType&lt;T&gt;</c> (HotChocolate) without <c>FilterBase&lt;T&gt;</c>
    /// in the base type chain, and is NOT in <c>SharedKernel.Presentation.GraphQL</c>,
    /// must fail
    /// <see cref="CommunicationLayeringRules.NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL"/>.
    /// </summary>
    [Fact]
    public void NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL_DirectFilterInputType_RuleFails()
    {
        const string source = """
            namespace HotChocolate.Data.Filters
            {
                // Stub simulating HotChocolate FilterInputType<T>
                public abstract class FilterInputType<T> { }
            }

            namespace Application.GraphQL
            {
                using HotChocolate.Data.Filters;

                // Violation: direct inheritance without FilterBase<T>
                public class OrderFilterType : FilterInputType<object> { }
            }
            """;

        var assembly = CompileInMemory("Fixture.GraphQL.FilterViolation", source);

        var result = CommunicationLayeringRules
            .NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderFilterType inherits from FilterInputType<T> directly without FilterBase<T> " +
                     "in its chain — violates the rule");
    }

    // ---------------------------------------------------------------------------
    // T-123 — Pass path: type inherits FilterBase<T> (which inherits FilterInputType<T>)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-123: A contrived assembly containing a class that inherits from <c>FilterBase&lt;T&gt;</c>
    /// (which in turn inherits <c>FilterInputType&lt;T&gt;</c>) must pass
    /// <see cref="CommunicationLayeringRules.NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL"/>
    /// because the platform wrapper is in the chain before the HotChocolate base.
    /// </summary>
    [Fact]
    public void NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL_FilterBaseInChain_RulePasses()
    {
        const string source = """
            namespace HotChocolate.Data.Filters
            {
                // Stub simulating HotChocolate FilterInputType<T>
                public abstract class FilterInputType<T> { }
            }

            namespace SharedKernel.Presentation.GraphQL
            {
                // Platform wrapper that legitimately inherits from FilterInputType<T>
                public abstract class FilterBase<T> : HotChocolate.Data.Filters.FilterInputType<T> { }
            }

            namespace Application.GraphQL
            {
                using SharedKernel.Presentation.GraphQL;

                // Compliant: inherits from FilterBase<T> (platform wrapper)
                public class OrderFilterType : FilterBase<object> { }
            }
            """;

        var assembly = CompileInMemory("Fixture.GraphQL.FilterCompliant", source);

        var result = CommunicationLayeringRules
            .NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderFilterType inherits from FilterBase<T> (platform wrapper), " +
                     "which appears before FilterInputType<T> in the chain");
    }

    // ---------------------------------------------------------------------------
    // T-127 — Fire path: contrived assembly references SharedKernel.Contracts
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-127: A contrived assembly shaped like <c>SharedKernel.Communication.Grpc</c> whose type
    /// references <c>SharedKernel.Contracts</c> must fail
    /// <see cref="CommunicationLayeringRules.GrpcNeverReferencesContracts"/> — this is the
    /// regression that P-163 (WO-026) removed and that this rule mechanically forbids from
    /// re-entering the codebase.
    /// </summary>
    [Fact]
    public void GrpcNeverReferencesContracts_ContractsReference_RuleFails()
    {
        const string contractsStubSource = """
            namespace SharedKernel.Contracts
            {
                public class PagedList<T> { }
            }
            """;

        const string grpcSource = """
            namespace SharedKernel.Communication.Grpc
            {
                public class GrpcResponseMapper
                {
                    // Violation: a dead reference into SharedKernel.Contracts, the regression
                    // mechanically forbidden by GrpcNeverReferencesContracts (WO-026 P-163).
                    private readonly SharedKernel.Contracts.PagedList<object> _paged;

                    public GrpcResponseMapper(SharedKernel.Contracts.PagedList<object> paged)
                    {
                        _paged = paged;
                    }
                }
            }
            """;

        var contractsAssembly = CompileInMemory(
            "Fixture.GrpcContracts.SharedKernel.Contracts.Stub",
            contractsStubSource);

        var grpcAssembly = CompileInMemory(
            "Fixture.GrpcContracts.ViolatingGrpc",
            grpcSource,
            extraReferences: new[] { contractsAssembly });

        var result = CommunicationLayeringRules
            .GrpcNeverReferencesContracts(grpcAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "GrpcResponseMapper depends on SharedKernel.Contracts — forbidden for " +
                     "SharedKernel.Communication.Grpc (WO-026 P-163)");
    }

    // ---------------------------------------------------------------------------
    // T-128 — Pass path: the real SharedKernel.Communication.Grpc assembly (post P-163)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-128: The real, currently-built <c>SharedKernel.Communication.Grpc</c> assembly — clean
    /// of the <c>SharedKernel.Contracts</c> dead reference removed in P-163 — must pass
    /// <see cref="CommunicationLayeringRules.GrpcNeverReferencesContracts"/> with zero violations.
    /// </summary>
    [Fact]
    public void GrpcNeverReferencesContracts_RealGrpcAssembly_RulePasses()
    {
        var grpcAssembly = typeof(SharedKernel.Communication.GrpcClientOptions)
            .Assembly;

        var result = CommunicationLayeringRules
            .GrpcNeverReferencesContracts(grpcAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Communication.Grpc has no dependency on SharedKernel.Contracts " +
                     "since the dead reference was removed in P-163 (WO-026)");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly for NetArchTest scanning.
    /// </summary>
    /// <remarks>
    /// Follows the pattern established in <see cref="RedisTopologyRulesTests"/>:
    /// extra references are provided via <c>MetadataReference.CreateFromImage</c> from the
    /// in-memory bytes to avoid <c>CS0234</c> failures when chaining fixture assemblies.
    /// </remarks>
    private static Assembly CompileInMemory(
        string assemblyName,
        string source,
        Assembly[]? extraReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        if (extraReferences is not null)
        {
            foreach (var extraReference in extraReferences)
            {
                references.Add(
                    MetadataReference.CreateFromImage(
                        System.Collections.Immutable.ImmutableArray.Create(
                            File.ReadAllBytes(extraReference.Location))));
            }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{assemblyName}_{System.Guid.NewGuid():N}.dll");

        using (var stream = new MemoryStream())
        {
            var emitResult = compilation.Emit(stream);
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

            stream.Seek(0, SeekOrigin.Begin);
            File.WriteAllBytes(tempPath, stream.ToArray());
        }

        return Assembly.LoadFrom(tempPath);
    }
}
