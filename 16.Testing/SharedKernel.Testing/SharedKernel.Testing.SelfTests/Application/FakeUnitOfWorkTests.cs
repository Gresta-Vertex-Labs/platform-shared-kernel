using SharedKernel.Testing.Application;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Application;

public sealed class FakeUnitOfWorkTests
{
    [Fact]
    public async Task SaveChangesAsync_IncrementsCallCount_OncePerCall()
    {
        var unitOfWork = new FakeUnitOfWork();

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        Assert.Equal(2, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task SaveChangesAsync_ReturnsConfiguredSaveChangesResult()
    {
        var unitOfWork = new FakeUnitOfWork { SaveChangesResult = 42 };

        var result = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task SaveChangesAsync_DefaultSaveChangesResult_IsOne()
    {
        var unitOfWork = new FakeUnitOfWork();

        var result = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task SaveChangesAsync_SimulateFailure_ThrowsInvalidOperationException()
    {
        var unitOfWork = new FakeUnitOfWork { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveChangesAsync_SimulateFailure_StillIncrementsCallCount()
    {
        var unitOfWork = new FakeUnitOfWork { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.SaveChangesAsync(CancellationToken.None));

        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }
}
