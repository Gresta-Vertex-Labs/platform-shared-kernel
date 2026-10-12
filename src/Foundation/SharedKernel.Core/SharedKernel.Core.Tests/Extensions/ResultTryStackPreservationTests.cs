using System.Runtime.CompilerServices;
using SharedKernel.Core.Extensions;
using Xunit;

namespace SharedKernel.Core.Tests.Extensions;

/// <summary>
/// Cancellation leaves <see cref="ResultTry"/> as the same exception instance, with the frames of its original
/// throw site (no catch-and-rethrow resets the trace).
/// </summary>
public sealed class ResultTryStackPreservationTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ThrowInstance(OperationCanceledException exception) => throw exception;

    [Fact]
    public void Try_Cancellation_IsRethrownAsTheSameInstance_WithItsOriginalTrace()
    {
        var original = new OperationCanceledException("canceled-here");

        var thrown = Assert.Throws<OperationCanceledException>(() => ResultTry.Try(() => ThrowInstance(original)));

        Assert.Same(original, thrown);
        Assert.Contains(nameof(ThrowInstance), thrown.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryAsync_Cancellation_IsRethrownAsTheSameInstance_WithItsOriginalTrace()
    {
        var original = new OperationCanceledException("canceled-here");

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => ResultTry.TryAsync(() => Task.FromResult(ThrowInstance(original))));

        Assert.Same(original, thrown);
        Assert.Contains(nameof(ThrowInstance), thrown.StackTrace, StringComparison.Ordinal);
    }
}
