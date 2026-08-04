using System.Data;
using NSubstitute;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

public sealed class FakeDbConnectionFactoryTests
{
    [Fact]
    public async Task CreateConnectionAsync_ReturnsExactConnection_ProducedBySuppliedDelegate()
    {
        var connection = Substitute.For<IDbConnection>();
        var factory = new FakeDbConnectionFactory(() => connection);

        var result = await factory.CreateConnectionAsync();

        Assert.Same(connection, result);
    }

    [Fact]
    public async Task CreateConnectionAsync_InvokesDelegateFresh_OnEveryCall()
    {
        var callCount = 0;
        var factory = new FakeDbConnectionFactory(() =>
        {
            callCount++;
            return Substitute.For<IDbConnection>();
        });

        var first = await factory.CreateConnectionAsync();
        var second = await factory.CreateConnectionAsync();
        var third = await factory.CreateConnectionAsync();

        // Three calls produced three delegate invocations — never a cached single result.
        Assert.Equal(3, callCount);
        Assert.NotSame(first, second);
        Assert.NotSame(second, third);
        Assert.NotSame(first, third);
    }

    [Fact]
    public void CreateConnectionAsync_ReturnsAlreadyCompletedTask()
    {
        var factory = new FakeDbConnectionFactory(() => Substitute.For<IDbConnection>());

        var task = factory.CreateConnectionAsync();

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FakeDbConnectionFactory(null!));
    }

    [Fact]
    public async Task CreateConnectionAsync_AlreadyCancelledToken_ThrowsSynchronously()
    {
        var factory = new FakeDbConnectionFactory(() => Substitute.For<IDbConnection>());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => factory.CreateConnectionAsync(cts.Token));
    }
}
