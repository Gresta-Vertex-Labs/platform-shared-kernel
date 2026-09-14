using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Core.Tests.Extensions;

/// <summary>Void and cancellation-token overloads of <see cref="ResultTry"/>.</summary>
public sealed class ResultTryVoidAndCancellationTests
{
    [Fact]
    public void Try_Action_Completes_ReturnsSuccess()
    {
        var ran = false;

        var result = ResultTry.Try(() => { ran = true; });

        Assert.True(result.IsSuccess);
        Assert.True(ran);
    }

    [Fact]
    public void Try_Action_Throws_ReturnsFixedSafeFailure()
    {
        var result = ResultTry.Try(() => throw new InvalidOperationException("secret host db01"));

        Assert.Equal(ErrorCodes.Unexpected.Default, result.Error.Code);
        Assert.Equal(ResultTry.DefaultUnexpectedMessage, result.Error.Message);
    }

    [Fact]
    public void Try_Action_WithMapper_UsesMapper()
    {
        var result = ResultTry.Try(
            () => throw new TimeoutException(),
            ex => Error.Unexpected("timeout", ex.GetType().Name));

        Assert.Equal("TimeoutException", result.Error.Message);
    }

    [Fact]
    public void Try_Action_OperationCanceled_Propagates()
        => Assert.Throws<OperationCanceledException>(
            () => ResultTry.Try(() => throw new OperationCanceledException()));

    [Fact]
    public async Task TryAsync_FuncTask_Faults_ReturnsFailure()
    {
        var result = await ResultTry.TryAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException();
        });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task TryAsync_FuncTask_Completes_ReturnsSuccess()
    {
        var result = await ResultTry.TryAsync(() => Task.CompletedTask);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TryAsync_WithToken_PassesTokenToOperation()
    {
        using var source = new CancellationTokenSource();
        CancellationToken received = default;

        var result = await ResultTry.TryAsync(
            ct =>
            {
                received = ct;
                return Task.FromResult(5);
            },
            source.Token);

        Assert.Equal(5, result.Value);
        Assert.Equal(source.Token, received);
    }

    [Fact]
    public async Task TryAsync_WithAlreadyCancelledToken_ThrowsWithoutRunningOperation()
    {
        var ran = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ResultTry.TryAsync(
            _ =>
            {
                ran = true;
                return Task.FromResult(1);
            },
            new CancellationToken(canceled: true)));

        Assert.False(ran);
    }

    [Fact]
    public async Task TryAsync_NonGenericWithToken_CancelledDuringOperation_Propagates()
    {
        using var source = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ResultTry.TryAsync(
            async ct =>
            {
                await source.CancelAsync();
                await Task.Delay(Timeout.Infinite, ct);
            },
            source.Token));
    }

    [Fact]
    public async Task TryAsync_WithTokenAndMapper_MapsFault()
    {
        var result = await ResultTry.TryAsync(
            Task<int> (_) => throw new FormatException(),
            _ => Error.Validation("fmt", "bad format"),
            CancellationToken.None);

        Assert.Equal("fmt", result.Error.Code);
    }
}
