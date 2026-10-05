using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.Testing.Intelligence;

namespace SharedKernel.Testing.SelfTests.Intelligence;

/// <summary>
/// Proves <see cref="InMemoryCompletionProviderDescriptor"/> against
/// <c>ICompletionProviderDescriptor</c>'s documented zero-I/O ceiling/context-window-guard contract --
/// no consuming domain has adopted this fake yet, so this self-test is the only behavioral proof
/// today, per the SelfTests routing rule.
/// </summary>
public sealed class InMemoryCompletionProviderDescriptorTests
{
    [Fact]
    public void Constructor_Defaults_UseDocumentedValues()
    {
        var descriptor = new InMemoryCompletionProviderDescriptor();

        Assert.Equal("in-memory-fake", descriptor.ProviderName);
        Assert.Equal(128000, descriptor.ContextWindowTokens);
        Assert.Equal(4096, descriptor.MaxOutputTokens);
    }

    [Fact]
    public void Constructor_CustomValues_AreUsed()
    {
        var descriptor = new InMemoryCompletionProviderDescriptor("custom-provider", 8000, 1000);

        Assert.Equal("custom-provider", descriptor.ProviderName);
        Assert.Equal(8000, descriptor.ContextWindowTokens);
        Assert.Equal(1000, descriptor.MaxOutputTokens);
    }

    [Fact]
    public void Constructor_NullProviderName_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemoryCompletionProviderDescriptor(null!));

    [Fact]
    public void ValidateContextWindow_WithinLimit_Succeeds()
    {
        var descriptor = new InMemoryCompletionProviderDescriptor(contextWindowTokens: 1000);

        var result = descriptor.ValidateContextWindow(500);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateContextWindow_ExactlyAtLimit_Succeeds()
    {
        var descriptor = new InMemoryCompletionProviderDescriptor(contextWindowTokens: 1000);

        var result = descriptor.ValidateContextWindow(1000);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateContextWindow_ExceedsLimit_ReturnsContextWindowExceeded()
    {
        var descriptor = new InMemoryCompletionProviderDescriptor(contextWindowTokens: 1000);

        var result = descriptor.ValidateContextWindow(1001);

        Assert.Equal(IntelligenceErrors.ContextWindowExceeded(1000, 1001), result.Error);
    }

    [Fact]
    public void ProviderName_ContextWindowTokens_MaxOutputTokens_AreSettable()
    {
        var descriptor = new InMemoryCompletionProviderDescriptor
        {
            ProviderName = "changed",
            ContextWindowTokens = 2000,
            MaxOutputTokens = 500,
        };

        Assert.Equal("changed", descriptor.ProviderName);
        Assert.Equal(2000, descriptor.ContextWindowTokens);
        Assert.Equal(500, descriptor.MaxOutputTokens);
    }
}
