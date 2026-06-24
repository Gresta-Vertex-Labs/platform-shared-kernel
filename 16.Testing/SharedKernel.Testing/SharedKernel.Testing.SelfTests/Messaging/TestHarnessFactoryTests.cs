using SharedKernel.Testing.Messaging;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Messaging;

public sealed class TestHarnessFactoryTests
{
    [Fact]
    public async Task CreateAsync_ReturnsStartedHarness()
    {
        var harness = await TestHarnessFactory.CreateAsync("self-tests-service");

        try
        {
            Assert.NotNull(harness);
        }
        finally
        {
            if (harness is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateAsync_NullOrWhitespaceServiceName_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() => TestHarnessFactory.CreateAsync(" "));
}
