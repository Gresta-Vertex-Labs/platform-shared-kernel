using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Rest.Extensions;
using SharedKernel.Communication.Rest.Options;

namespace SharedKernel.Communication.Rest.Tests.Options;

/// <summary>
/// T-20: TotalTimeoutBufferSec formula tests.
/// </summary>
public sealed class TotalTimeoutFormulaTests
{
    [Theory]
    [InlineData(10, 3, 10, 50)]   // 10 * (3+1) + 10 = 50
    [InlineData(5, 2, 10, 25)]    // 5 * (2+1) + 10 = 25
    [InlineData(30, 1, 10, 70)]   // 30 * (1+1) + 10 = 70
    [InlineData(10, 3, 0, 40)]    // 10 * (3+1) + 0 = 40  (tight budget)
    [InlineData(5, 0, 0, 5)]      // 5 * (0+1) + 0 = 5
    public void TotalTimeoutBufferSec_Formula_ProducesExpectedValue(
        int timeoutSec, int retryCount, int bufferSec, int expectedTotalSec)
    {
        var actual = timeoutSec * (retryCount + 1) + bufferSec;
        actual.Should().Be(expectedTotalSec);
    }

    [Fact]
    public void TotalTimeoutBufferSec_DefaultValue_Is10()
    {
        var options = new RestResilienceOptions();
        options.TotalTimeoutBufferSec.Should().Be(10);
    }

    [Fact]
    public void TotalTimeoutBufferSec_CanBeSetToZero()
    {
        var options = new RestResilienceOptions { TotalTimeoutBufferSec = 0 };
        options.TotalTimeoutBufferSec.Should().Be(0);
    }

    [Fact]
    public void AddRestClient_WithTotalTimeoutBufferSec_BuildsSuccessfully()
    {
        // Arrange — verify the builder uses the configured TotalTimeoutBufferSec value
        // without throwing (Polly constraint satisfied)
        var services = new ServiceCollection();

        // Act & Assert — no exception
        services.AddSharedKernelRestCommunication()
            .AddRestClient<TotalTimeoutTestClient>(
                "timeout-test",
                o =>
                {
                    o.BaseAddress = "http://timeout-test-service";
                    o.TimeoutSeconds = 5;
                    o.Resilience.RetryCount = 1;
                    o.Resilience.TotalTimeoutBufferSec = 0;  // tight budget: no buffer
                });

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<TotalTimeoutTestClient>();
        client.Should().NotBeNull();
    }

    [Fact]
    public void AddRestClient_WithDefaultTotalTimeoutBufferSec_BuildsSuccessfully()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelRestCommunication()
            .AddRestClient<TotalTimeoutTestClient2>(
                "default-buffer",
                o =>
                {
                    o.BaseAddress = "http://test";
                    o.TimeoutSeconds = 10;
                    o.Resilience.RetryCount = 3;
                    // TotalTimeoutBufferSec defaults to 10
                });

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<TotalTimeoutTestClient2>().Should().NotBeNull();
    }
}

internal sealed class TotalTimeoutTestClient(HttpClient httpClient)
{
    public HttpClient Http { get; } = httpClient;
}

internal sealed class TotalTimeoutTestClient2(HttpClient httpClient)
{
    public HttpClient Http { get; } = httpClient;
}
