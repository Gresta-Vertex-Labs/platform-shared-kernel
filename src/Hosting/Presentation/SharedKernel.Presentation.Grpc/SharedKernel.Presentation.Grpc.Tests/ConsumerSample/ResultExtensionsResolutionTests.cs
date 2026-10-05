// P-562 R32 — a compile-level proof, written the way a consuming service writes it: both namespaces imported at the
// top of the file. Until R32, SharedKernel.Presentation.Grpc declared ThrowIfFailure/GetValueOrThrow with the same
// signatures as SharedKernel.Core's, and every call below was error CS0121 (the call is ambiguous). This file compiling
// is the proof.
//
// The namespace is deliberately not below SharedKernel.Presentation.Grpc: from inside it, the package's own extensions
// would have won without ambiguity, so the file would have compiled even with the collision in place.
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using SharedKernel.Core.Exceptions;
using SharedKernel.Core.Extensions;
using SharedKernel.Presentation.Grpc;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace ConsumerSample.Orders.Api;

/// <summary>A consuming service's view of the gRPC package and <c>SharedKernel.Core</c>'s result extensions together.</summary>
public sealed class ResultExtensionsResolutionTests
{
    private static readonly Error NotFound = Error.NotFound("order.not_found", "Order 42 was not found.");

    [Fact]
    public async Task CoreAndGrpcNamespacesTogether_Compile_AndResolveToSharedKernelCore()
    {
        // Program.cs: the gRPC package's registration...
        var builder = WebApplication.CreateBuilder();
        builder.AddSharedKernelGrpc();

        // ...and a service method ending its results with Core's extensions: success returns the value...
        Result.Success().ThrowIfFailure();
        Result<int>.Success(42).GetValueOrThrow().Should().Be(42);
        await Task.FromResult(Result.Success()).ThrowIfFailure();
        (await Task.FromResult(Result<int>.Success(7)).GetValueOrThrow()).Should().Be(7);

        // ...and a failure throws the error's SharedKernelException — Core's behavior, which the interceptor maps to the
        // rich status — never the RpcException the removed package extensions threw.
        var sync = () => Result.Failure(NotFound).ThrowIfFailure();
        var syncValue = () => Result<int>.Failure(NotFound).GetValueOrThrow();
        var onTask = () => Task.FromResult(Result.Failure(NotFound)).ThrowIfFailure();
        var onTaskValue = () => Task.FromResult(Result<int>.Failure(NotFound)).GetValueOrThrow();

        sync.Should().Throw<NotFoundException>().Which.Error.Should().Be(NotFound);
        syncValue.Should().Throw<NotFoundException>().Which.Error.Should().Be(NotFound);
        (await onTask.Should().ThrowAsync<NotFoundException>()).Which.Error.Should().Be(NotFound);
        (await onTaskValue.Should().ThrowAsync<NotFoundException>()).Which.Error.Should().Be(NotFound);
    }

    [Fact]
    public void TheGrpcPackage_DeclaresNoResultExtension_ThatCouldCollideWithCores()
    {
        var collisions = typeof(GrpcHostBuilderExtensions).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.IsDefined(typeof(ExtensionAttribute), inherit: false))
            .Where(method => method.Name is nameof(ResultExtensions.ThrowIfFailure) or nameof(ResultExtensions.GetValueOrThrow))
            .Select(method => $"{method.DeclaringType}.{method.Name}");

        collisions.Should().BeEmpty("SharedKernel.Core.Extensions.ResultExtensions owns these names (P-562 R32)");
    }
}
