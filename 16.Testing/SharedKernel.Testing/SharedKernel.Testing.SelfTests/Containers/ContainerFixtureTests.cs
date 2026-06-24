using SharedKernel.Testing.Containers;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Containers;

public sealed class ContainerFixtureTests
{
    [Fact]
    public void PostgreSqlContainerFixture_ConnectionString_ReadBeforeInitialize_Throws()
    {
        var fixture = new PostgreSqlContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.ConnectionString);
    }

    [Fact]
    public async Task PostgreSqlContainerFixture_FullLifecycle_StartsAndStops()
    {
        var fixture = new PostgreSqlContainerFixture();

        await fixture.InitializeAsync();
        try
        {
            var connectionString = fixture.ConnectionString;
            Assert.False(string.IsNullOrWhiteSpace(connectionString));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public void RedisContainerFixture_ConnectionString_ReadBeforeInitialize_Throws()
    {
        var fixture = new RedisContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.ConnectionString);
    }

    [Fact]
    public async Task RedisContainerFixture_FullLifecycle_StartsAndStops()
    {
        var fixture = new RedisContainerFixture();

        await fixture.InitializeAsync();
        try
        {
            var connectionString = fixture.ConnectionString;
            Assert.False(string.IsNullOrWhiteSpace(connectionString));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public void RabbitMqContainerFixture_ConnectionString_ReadBeforeInitialize_Throws()
    {
        var fixture = new RabbitMqContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.ConnectionString);
    }

    [Fact]
    public async Task RabbitMqContainerFixture_FullLifecycle_StartsAndStops()
    {
        var fixture = new RabbitMqContainerFixture();

        await fixture.InitializeAsync();
        try
        {
            var connectionString = fixture.ConnectionString;
            Assert.False(string.IsNullOrWhiteSpace(connectionString));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
