using FluentAssertions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Storage.Abstractions.Tests;

/// <summary>
/// T-04: reflection-based contract shape tests, mirroring <c>06.Persistence</c>'s
/// <c>ContractShapeTests</c> precedent. Locks the two most novel <see cref="IFileStorage"/>
/// signatures against silent regression: <see cref="IFileStorage.ListAsync"/> must remain a
/// constant-memory <see cref="IAsyncEnumerable{T}"/> (never the originally-sketched
/// <c>Task&lt;Result&lt;IReadOnlyList&lt;FileMetadata&gt;&gt;&gt;</c> shape), and
/// <see cref="IFileStorage.CheckHealthAsync"/> must remain a non-generic <see cref="Result"/>
/// (never <c>Result&lt;T&gt;</c>).
/// </summary>
public sealed class ContractShapeTests
{
    [Fact]
    public void IFileStorage_ListAsync_Returns_IAsyncEnumerableOfFileMetadata()
    {
        var method = typeof(IFileStorage).GetMethod(nameof(IFileStorage.ListAsync));

        method.Should().NotBeNull();
        method!.ReturnType.IsGenericType.Should().BeTrue();
        method.ReturnType.GetGenericTypeDefinition().Should().Be(typeof(IAsyncEnumerable<>));
        method.ReturnType.GetGenericArguments().Should().ContainSingle().Which.Should().Be(typeof(FileMetadata));
    }

    [Fact]
    public void IFileStorage_ListAsync_IsNot_TaskWrapped()
    {
        var method = typeof(IFileStorage).GetMethod(nameof(IFileStorage.ListAsync))!;

        method.ReturnType.Should().NotBe(typeof(Task<Result<IReadOnlyList<FileMetadata>>>),
            "ListAsync must never regress to the originally-sketched fully-materialized shape");
    }

    [Fact]
    public void IFileStorage_CheckHealthAsync_Returns_TaskOfNonGenericResult()
    {
        var method = typeof(IFileStorage).GetMethod(nameof(IFileStorage.CheckHealthAsync));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<Result>),
            "CheckHealthAsync must return a non-generic Task<Result>, not a Task<Result<T>>");
    }

    [Fact]
    public void IFileStorage_HasExactlyNineMembers()
    {
        var methods = typeof(IFileStorage).GetMethods();

        methods.Should().HaveCount(9,
            "IFileStorage is locked at nine members: Upload/Download/Delete/Exists/GetMetadata " +
            "(unchanged) plus Copy/DeleteMany/ListAsync/CheckHealthAsync");
    }

    [Theory]
    [InlineData(nameof(IFileStorage.UploadAsync))]
    [InlineData(nameof(IFileStorage.DownloadAsync))]
    [InlineData(nameof(IFileStorage.DeleteAsync))]
    [InlineData(nameof(IFileStorage.ExistsAsync))]
    [InlineData(nameof(IFileStorage.GetMetadataAsync))]
    [InlineData(nameof(IFileStorage.CopyAsync))]
    [InlineData(nameof(IFileStorage.DeleteManyAsync))]
    [InlineData(nameof(IFileStorage.ListAsync))]
    [InlineData(nameof(IFileStorage.CheckHealthAsync))]
    public void IFileStorage_DeclaresExpectedMember(string memberName)
    {
        typeof(IFileStorage).GetMethod(memberName).Should().NotBeNull($"IFileStorage must declare {memberName}");
    }

    [Fact]
    public void IBlobUriGenerator_HasExactlyTwoMembers()
    {
        var methods = typeof(IBlobUriGenerator).GetMethods();

        methods.Should().HaveCount(2,
            "IBlobUriGenerator is locked at two members: GeneratePresignedUploadUrl/GeneratePresignedDownloadUrl");
    }

    [Fact]
    public void IBlobUriGenerator_Members_AreSynchronous_NotAsync()
    {
        // Presigning is a local cryptographic operation — no network round-trip — so neither member
        // returns a Task/ValueTask.
        var methods = typeof(IBlobUriGenerator).GetMethods();

        methods.Should().OnlyContain(m => m.ReturnType == typeof(Result<PresignedUrl>));
    }

    [Fact]
    public void Abstractions_Assembly_HasNoThirdPartyNuGetDependency()
    {
        var assembly = typeof(IFileStorage).Assembly;

        var referencedAssemblyNames = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        referencedAssemblyNames.Should().OnlyContain(
            name => name!.StartsWith("System.", StringComparison.Ordinal)
                || name.StartsWith("netstandard", StringComparison.Ordinal)
                || name.StartsWith("mscorlib", StringComparison.Ordinal)
                || name == "SharedKernel.Primitives",
            "SharedKernel.Storage.Abstractions must reference only the BCL and SharedKernel.Primitives — " +
            "zero third-party NuGet dependencies");
    }
}
