using FluentAssertions;
using SharedKernel.AI.SemanticKernel.Diagnostics;

namespace SharedKernel.AI.SemanticKernel.Tests.Diagnostics;

public sealed class SemanticKernelProviderDescriptorTests
{
    [Fact]
    public void ValidateContextWindow_WithinLimit_Succeeds()
    {
        var descriptor = new SemanticKernelProviderDescriptor(contextWindowTokens: 128_000, maxOutputTokens: 4096);

        var result = descriptor.ValidateContextWindow(1000);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateContextWindow_ExceedsLimit_ReturnsContextWindowExceeded()
    {
        var descriptor = new SemanticKernelProviderDescriptor(contextWindowTokens: 128_000, maxOutputTokens: 4096);

        var result = descriptor.ValidateContextWindow(200_000);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.context_window_exceeded");
    }

    [Fact]
    public void ValidateContextWindow_ExactlyAtLimit_Succeeds()
    {
        var descriptor = new SemanticKernelProviderDescriptor(contextWindowTokens: 128_000, maxOutputTokens: 4096);

        var result = descriptor.ValidateContextWindow(128_000);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ProviderName_IsSemanticKernel()
    {
        var descriptor = new SemanticKernelProviderDescriptor(contextWindowTokens: 128_000, maxOutputTokens: 4096);

        descriptor.ProviderName.Should().Be("semantickernel");
    }
}
