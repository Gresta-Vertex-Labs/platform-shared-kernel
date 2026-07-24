using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.AI.Qdrant.Options;
using SharedKernel.Configuration.Extensions;

namespace SharedKernel.AI.Qdrant.Tests.Options;

public sealed class QdrantOptionsTests
{
    [Fact]
    public void ValidConfiguration_ResolvesWithoutThrowing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{QdrantOptions.SectionName}:Host"] = "localhost",
                [$"{QdrantOptions.SectionName}:Port"] = "6334",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddValidatedOptions<QdrantOptions>(config.GetSection(QdrantOptions.SectionName));
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<QdrantOptions>>().Value;

        act.Should().NotThrow();
        act().Host.Should().Be("localhost");
        act().Port.Should().Be(6334);
    }

    [Fact]
    public void MissingHost_ThrowsOptionsValidationException_NamingTheProperty()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var services = new ServiceCollection();
        services.AddValidatedOptions<QdrantOptions>(config.GetSection(QdrantOptions.SectionName));
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<QdrantOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().Where(ex => ex.Message.Contains("Host"));
    }

    [Fact]
    public void PortOutOfRange_ThrowsOptionsValidationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{QdrantOptions.SectionName}:Host"] = "localhost",
                [$"{QdrantOptions.SectionName}:Port"] = "0",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddValidatedOptions<QdrantOptions>(config.GetSection(QdrantOptions.SectionName));
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<QdrantOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().Where(ex => ex.Message.Contains("Port"));
    }

    [Fact]
    public void Defaults_AreSaneWhenOnlyRequiredFieldsSupplied()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{QdrantOptions.SectionName}:Host"] = "localhost" })
            .Build();

        var services = new ServiceCollection();
        services.AddValidatedOptions<QdrantOptions>(config.GetSection(QdrantOptions.SectionName));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QdrantOptions>>().Value;

        options.Port.Should().Be(6334);
        options.UseTls.Should().BeFalse();
        options.MaxBatchSize.Should().Be(1000);
        options.MaxVectorDimension.Should().Be(4096);
        options.MaxFilterDepth.Should().Be(10);
    }
}
