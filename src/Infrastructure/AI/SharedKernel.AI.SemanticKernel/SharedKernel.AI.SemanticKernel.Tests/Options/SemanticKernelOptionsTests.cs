using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.AI.SemanticKernel.Options;
using SharedKernel.Configuration.Extensions;

namespace SharedKernel.AI.SemanticKernel.Tests.Options;

public sealed class SemanticKernelOptionsTests
{
    private static Dictionary<string, string?> ValidConfigDictionary() => new()
    {
        [$"{SemanticKernelOptions.SectionName}:ApiKey"] = "test-key",
        [$"{SemanticKernelOptions.SectionName}:ChatModelId"] = "gpt-test",
        [$"{SemanticKernelOptions.SectionName}:EmbeddingModelId"] = "text-embedding-3-small",
    };

    [Fact]
    public void ValidConfiguration_ResolvesWithoutThrowing()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(ValidConfigDictionary()).Build();
        var services = new ServiceCollection();
        services.AddValidatedOptions<SemanticKernelOptions>(config.GetSection(SemanticKernelOptions.SectionName));
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<SemanticKernelOptions>>().Value;

        act.Should().NotThrow();
        act().ChatModelId.Should().Be("gpt-test");
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("ChatModelId")]
    [InlineData("EmbeddingModelId")]
    public void MissingRequiredField_ThrowsOptionsValidationException_NamingTheProperty(string missingKey)
    {
        var dictionary = ValidConfigDictionary();
        dictionary.Remove($"{SemanticKernelOptions.SectionName}:{missingKey}");
        var config = new ConfigurationBuilder().AddInMemoryCollection(dictionary).Build();

        var services = new ServiceCollection();
        services.AddValidatedOptions<SemanticKernelOptions>(config.GetSection(SemanticKernelOptions.SectionName));
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<SemanticKernelOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().Where(ex => ex.Message.Contains(missingKey));
    }

    [Fact]
    public void Defaults_AreSaneWhenOnlyRequiredFieldsSupplied()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(ValidConfigDictionary()).Build();
        var services = new ServiceCollection();
        services.AddValidatedOptions<SemanticKernelOptions>(config.GetSection(SemanticKernelOptions.SectionName));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SemanticKernelOptions>>().Value;

        options.EmbeddingDimension.Should().Be(1536);
        options.ContextWindowTokens.Should().Be(128_000);
        options.MaxOutputTokens.Should().Be(4096);
        options.MaxEmbeddingBatchSize.Should().Be(2048);
        options.HttpTimeoutSeconds.Should().Be(60);
        options.Endpoint.Should().BeNull();
    }
}
