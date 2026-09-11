using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using Xunit;

namespace SharedKernel.Configuration.Tests.Options;

/// <summary>
/// Covers the overloads that read the section path from the options type itself via
/// <see cref="ISectionBoundOptions"/>, so no call site names it.
/// </summary>
public sealed partial class SectionBoundOptionsTests
{
    [Fact]
    public void BindsFromTheSectionTheTypeDeclares()
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions<DeclaredSectionOptions>(Configuration());

        DeclaredSectionOptions value = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<DeclaredSectionOptions>>()
            .Value;

        Assert.Equal("from-declared-section", value.Name);
        Assert.Equal(42, value.Timeout);
    }

    [Fact]
    public void DoesNotBindFromAnUnrelatedSectionOfTheSameShape()
    {
        // Proves the section path is genuinely taken from SectionName rather than guessed from
        // the type name or from the root.
        var services = new ServiceCollection();
        services.AddValidatedOptions<DeclaredSectionOptions>(Configuration());

        DeclaredSectionOptions value = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<DeclaredSectionOptions>>()
            .Value;

        Assert.NotEqual("from-decoy-section", value.Name);
    }

    [Fact]
    public async Task InvalidConfiguration_StillFailsAtHostStartup()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
                services.AddValidatedOptions<DeclaredSectionOptions>(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            // Name is [Required] and absent.
                            ["SharedKernel:Tests:Declared:Timeout"] = "42",
                        })
                        .Build()))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task WithAGeneratedValidator_BindsAndValidatesWithNoSectionPathAtTheCallSite()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
                services.AddValidatedOptions<DeclaredSectionOptions, DeclaredSectionOptionsValidator>(
                    Configuration()))
            .Build();

        await host.StartAsync();
        await host.StopAsync();

        Assert.Equal(
            "from-declared-section",
            host.Services.GetRequiredService<IOptions<DeclaredSectionOptions>>().Value.Name);
    }

    [Fact]
    public async Task WithAGeneratedValidator_InvalidConfiguration_FailsAtHostStartup()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
                services.AddValidatedOptions<DeclaredSectionOptions, DeclaredSectionOptionsValidator>(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["SharedKernel:Tests:Declared:Name"] = "ok",
                            ["SharedKernel:Tests:Declared:Timeout"] = "0", // [Range(1, 300)]
                        })
                        .Build()))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void AnExplicitSectionArgumentOverridesTheDeclaredOne()
    {
        // A type implementing ISectionBoundOptions can still be pointed at a different section:
        // the explicit-section overload is the more specific one and wins overload resolution.
        var services = new ServiceCollection();
        services.AddValidatedOptions<DeclaredSectionOptions>(
            Configuration().GetSection("SharedKernel:Tests:Decoy"));

        DeclaredSectionOptions value = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<DeclaredSectionOptions>>()
            .Value;

        Assert.Equal("from-decoy-section", value.Name);
    }

    [Fact]
    public void NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(
            () => services.AddValidatedOptions<DeclaredSectionOptions>((IConfiguration)null!));
        Assert.Throws<ArgumentNullException>(
            () => services.AddValidatedOptions<DeclaredSectionOptions, DeclaredSectionOptionsValidator>(
                (IConfiguration)null!));
    }

    private static IConfiguration Configuration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Tests:Declared:Name"] = "from-declared-section",
                ["SharedKernel:Tests:Declared:Timeout"] = "42",
                ["SharedKernel:Tests:Decoy:Name"] = "from-decoy-section",
                ["SharedKernel:Tests:Decoy:Timeout"] = "7",
            })
            .Build();

    /// <summary>
    /// Note the shape adoption requires: <c>SectionName</c> is a <c>static</c> property, not a
    /// <c>const</c> field, because a field cannot satisfy a <c>static abstract</c> property. It
    /// remains readable as <c>DeclaredSectionOptions.SectionName</c>.
    /// </summary>
    public sealed class DeclaredSectionOptions : ISectionBoundOptions
    {
        public static string SectionName => "SharedKernel:Tests:Declared";

        [Required]
        public string Name { get; set; } = string.Empty;

        [Range(1, 300)]
        public int Timeout { get; set; }
    }

    [OptionsValidator]
    public sealed partial class DeclaredSectionOptionsValidator
        : IValidateOptions<DeclaredSectionOptions>
    {
    }
}
