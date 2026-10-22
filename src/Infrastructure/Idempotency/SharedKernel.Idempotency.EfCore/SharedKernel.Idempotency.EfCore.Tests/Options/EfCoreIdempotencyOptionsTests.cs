using SharedKernel.Idempotency.EfCore.Options;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Options;

public sealed class EfCoreIdempotencyOptionsTests
{
    [Fact]
    public void AllowExecutionOnStoreUnavailable_DefaultsToFalse()
    {
        Assert.False(new EfCoreIdempotencyOptions().AllowExecutionOnStoreUnavailable);
    }

    [Fact]
    public void MigrateOnStartup_DefaultsToFalse_AndTheLockTimeoutToTheDefault()
    {
        var options = new EfCoreIdempotencyOptions();

        Assert.False(options.MigrateOnStartup);
        Assert.Null(options.MigrationLockTimeout);
    }

    [Fact]
    public void SectionName_IsTheDocumentedPath() =>
        Assert.Equal("SharedKernel:Idempotency:EfCore", EfCoreIdempotencyOptions.SectionName);
}
