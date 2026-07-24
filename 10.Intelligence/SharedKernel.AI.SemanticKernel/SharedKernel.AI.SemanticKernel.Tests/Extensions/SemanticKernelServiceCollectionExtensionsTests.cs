using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.SemanticKernel.Extensions;
using SharedKernel.AI.SemanticKernel.Options;
using SharedKernel.AI.SemanticKernel.Plugins;
using SharedKernel.AI.SemanticKernel.Raw;

namespace SharedKernel.AI.SemanticKernel.Tests.Extensions;

public sealed class SemanticKernelServiceCollectionExtensionsTests
{
    private static IConfiguration BuildValidConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SemanticKernelOptions.SectionName}:ApiKey"] = "test-key",
            [$"{SemanticKernelOptions.SectionName}:ChatModelId"] = "gpt-test",
            [$"{SemanticKernelOptions.SectionName}:EmbeddingModelId"] = "text-embedding-3-small",
        })
        .Build();

    private static ServiceProvider BuildProvider(Action<SemanticKernelBuilder>? configureBuilder = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var builder = services.AddSharedKernelSemanticKernel(BuildValidConfig());
        configureBuilder?.Invoke(builder);
        builder.Build();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void OpenAIClient_Resolves_AsSingleton_SameInstanceAcrossResolutions()
    {
        var provider = BuildProvider();

        var first = provider.GetRequiredService<OpenAIClient>();
        var second = provider.GetRequiredService<OpenAIClient>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void IEmbeddingGenerator_Resolves_AsSingleton()
    {
        var provider = BuildProvider();

        var first = provider.GetRequiredService<IEmbeddingGenerator>();
        var second = provider.GetRequiredService<IEmbeddingGenerator>();

        first.Should().BeSameAs(second);
        first.ModelId.Should().Be("text-embedding-3-small");
        first.Dimension.Should().Be(1536);
    }

    [Fact]
    public void ISemanticKernel_Resolves_AsSingleton()
    {
        var provider = BuildProvider();

        var first = provider.GetRequiredService<ISemanticKernel>();
        var second = provider.GetRequiredService<ISemanticKernel>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void ICompletionProviderDescriptor_Resolves_WithConfiguredCeilings()
    {
        var provider = BuildProvider();

        var descriptor = provider.GetRequiredService<ICompletionProviderDescriptor>();

        descriptor.ProviderName.Should().Be("semantickernel");
        descriptor.ContextWindowTokens.Should().Be(128_000);
        descriptor.MaxOutputTokens.Should().Be(4096);
    }

    [Fact]
    public void IChatCompletionService_Resolves_AsSingleton()
    {
        var provider = BuildProvider();

        var first = provider.GetRequiredService<IChatCompletionService>();
        var second = provider.GetRequiredService<IChatCompletionService>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void IKernelPluginAccessor_AlwaysResolves()
    {
        var provider = BuildProvider();

        var accessor = provider.GetService<IKernelPluginAccessor>();

        accessor.Should().NotBeNull();
        accessor!.Kernel.Should().NotBeNull();
    }

    [Fact]
    public void IKernelRawClientAccessor_IsNotResolvable_WithoutAllowRawClientAccess()
    {
        var provider = BuildProvider();

        var accessor = provider.GetService<IKernelRawClientAccessor>();

        accessor.Should().BeNull();
    }

    [Fact]
    public void IKernelRawClientAccessor_IsResolvable_WithAllowRawClientAccess()
    {
        var provider = BuildProvider(b => b.AllowRawClientAccess());

        var accessor = provider.GetService<IKernelRawClientAccessor>();

        accessor.Should().NotBeNull();
    }
}
