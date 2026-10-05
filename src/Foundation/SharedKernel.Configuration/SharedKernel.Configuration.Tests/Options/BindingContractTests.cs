using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using Xunit;

namespace SharedKernel.Configuration.Tests.Options;

/// <summary>
/// Pins the remaining documented contract: an unusable <see cref="ISectionBoundOptions.SectionName"/>
/// is rejected at registration, a value that cannot be converted fails startup as
/// <see cref="InvalidOperationException"/>, and nested objects are validated only when marked.
/// </summary>
public sealed partial class BindingContractTests
{
    // ---- SectionName guard ----

    [Fact]
    public void NullSectionName_ThrowsAtRegistration_NamingTheType()
    {
        // Before the guard: ArgumentNullException for a "path" parameter the caller never passed.
        var services = new ServiceCollection();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => services.AddValidatedOptions<NullSectionNameOptions>(new ConfigurationBuilder().Build()));

        Assert.Contains(typeof(NullSectionNameOptions).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains("returned null", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptySectionName_ThrowsAtRegistration()
    {
        // Before the guard: bound nothing and started the host on defaults.
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddValidatedOptions<EmptySectionNameOptions>(new ConfigurationBuilder().Build()));
    }

    [Fact]
    public void WhitespaceSectionName_ThrowsAtRegistration_OnTheValidatorOverloadToo()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddValidatedOptions<WhitespaceSectionNameOptions, WhitespaceSectionNameOptionsValidator>(
                new ConfigurationBuilder().Build()));
    }

    // ---- Conversion failures ----

    [Fact]
    public async Task UnconvertibleValue_FailsStartupAsInvalidOperationException_NotValidation()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["S:Port"] = "abc" })
            .Build();
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddValidatedOptions<PortOptions>(config.GetSection("S"));
        using IHost host = builder.Build();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.IsNotType<OptionsValidationException>(ex);
        Assert.Contains("'abc'", ex.Message, StringComparison.Ordinal);
    }

    // ---- Nested validation ----

    [Fact]
    public void NestedRequired_WithoutValidateObjectMembers_IsNotEnforced()
    {
        // The trap the XML docs warn about, pinned so the warning cannot outlive the behaviour.
        ServiceProvider provider = Registered(s => s.AddValidatedOptions<UnmarkedOuter>(NestedSection()));

        UnmarkedOuter value = provider.GetRequiredService<IOptions<UnmarkedOuter>>().Value;

        Assert.Null(value.Inner!.Host);
    }

    [Fact]
    public void NestedRequired_WithValidateObjectMembers_IsEnforcedByDataAnnotations()
    {
        ServiceProvider provider = Registered(s => s.AddValidatedOptions<MarkedOuter>(NestedSection()));

        OptionsValidationException ex = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<MarkedOuter>>().Value);

        Assert.Contains(ex.Failures, f => f.Contains("Host", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedRequired_WithValidateObjectMembers_IsEnforcedByAGeneratedValidator()
    {
        ServiceProvider provider = Registered(
            s => s.AddValidatedOptions<MarkedOuter, MarkedOuterValidator>(NestedSection()));

        OptionsValidationException ex = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<MarkedOuter>>().Value);

        Assert.Contains(ex.Failures, f => f.Contains("Host", StringComparison.Ordinal));
    }

    [Fact]
    public void CollectionItemRequired_WithValidateEnumeratedItems_IsEnforced()
    {
        ServiceProvider provider = Registered(s => s.AddValidatedOptions<MarkedOuter>(NestedSection()));

        OptionsValidationException ex = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<MarkedOuter>>().Value);

        Assert.Contains(ex.Failures, f => f.Contains("Items[0]", StringComparison.Ordinal));
    }

    private static ServiceProvider Registered(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return services.BuildServiceProvider();
    }

    /// <summary>A nested object and one collection item, each present but missing its [Required] Host.</summary>
    private static IConfigurationSection NestedSection()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["S:Inner:Port"] = "1",
                ["S:Items:0:Port"] = "1",
            })
            .Build()
            .GetSection("S");

    public sealed class NullSectionNameOptions : ISectionBoundOptions
    {
        public static string SectionName => null!;
    }

    public sealed class EmptySectionNameOptions : ISectionBoundOptions
    {
        public static string SectionName => string.Empty;
    }

    public sealed class WhitespaceSectionNameOptions : ISectionBoundOptions
    {
        public static string SectionName => "   ";
    }

    public sealed class WhitespaceSectionNameOptionsValidator : IValidateOptions<WhitespaceSectionNameOptions>
    {
        public ValidateOptionsResult Validate(string? name, WhitespaceSectionNameOptions options)
            => ValidateOptionsResult.Success;
    }

    public sealed class PortOptions
    {
        public int Port { get; set; }
    }

    public sealed class Endpoint
    {
        [Required]
        public string? Host { get; set; }

        public int Port { get; set; }
    }

    public sealed class UnmarkedOuter
    {
        public Endpoint? Inner { get; set; }
    }

    public sealed class MarkedOuter
    {
        [ValidateObjectMembers]
        public Endpoint? Inner { get; set; }

        [ValidateEnumeratedItems]
        public List<Endpoint> Items { get; set; } = [];
    }

    [OptionsValidator]
    public sealed partial class MarkedOuterValidator : IValidateOptions<MarkedOuter>
    {
    }
}
