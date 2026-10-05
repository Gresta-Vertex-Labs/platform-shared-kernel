using SharedKernel.Idempotency.Redis.Options;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Options;

public sealed class RedisIdempotencyOptionsTests
{
    [Fact]
    public void AllowExecutionOnStoreUnavailable_DefaultsToFalse()
    {
        var options = new RedisIdempotencyOptions();

        Assert.False(options.AllowExecutionOnStoreUnavailable);
    }

    [Fact]
    public void SectionName_IsTheDocumentedPath() =>
        Assert.Equal("SharedKernel:Idempotency:Redis", RedisIdempotencyOptions.SectionName);
}
