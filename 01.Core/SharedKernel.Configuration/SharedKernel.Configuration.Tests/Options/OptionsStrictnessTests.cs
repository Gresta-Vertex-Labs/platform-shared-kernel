using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using Xunit;

namespace SharedKernel.Configuration.Tests.Options;

/// <summary>
/// Pins <see cref="OptionsStrictness"/>: both checks are off by default, each turns a specific
/// configuration mistake into a startup failure of a documented exception type, and both are
/// forwarded by all four overloads.
/// </summary>
public sealed partial class OptionsStrictnessTests
{
    // ---- Defaults: both mistakes start the host silently unless a check is requested ----

    [Fact]
    public async Task MisspelledSectionPath_WithoutStrictness_StartsOnDefaults()
    {
        IConfiguration config = Memory(("Database:Timeout", "30"));

        using IHost host = await StartedHost(s =>
            s.AddValidatedOptions<LenientOptions>(config.GetSection("Databse")));

        Assert.Equal(10, host.Services.GetRequiredService<IOptions<LenientOptions>>().Value.Timeout);
    }

    [Fact]
    public async Task MisspelledKey_WithoutStrictness_StartsAndIgnoresTheKey()
    {
        IConfiguration config = Memory(("Database:Timeuot", "30"));

        using IHost host = await StartedHost(s =>
            s.AddValidatedOptions<LenientOptions>(config.GetSection("Database")));

        Assert.Equal(10, host.Services.GetRequiredService<IOptions<LenientOptions>>().Value.Timeout);
    }

    // ---- RequireSection ----

    [Fact]
    public async Task RequireSection_MissingSection_FailsStartupNamingThePath()
    {
        IConfiguration config = Memory(("Database:Timeout", "30"));

        OptionsValidationException ex = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            StartedHost(s => s.AddValidatedOptions<LenientOptions>(
                config.GetSection("Databse"), strictness: OptionsStrictness.RequireSection)));

        string failure = Assert.Single(ex.Failures);
        Assert.Contains("'Databse'", failure, StringComparison.Ordinal);
        Assert.Contains(nameof(OptionsStrictness.RequireSection), failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequireSection_PresentSection_Starts()
    {
        IConfiguration config = Memory(("Database:Timeout", "30"));

        using IHost host = await StartedHost(s => s.AddValidatedOptions<LenientOptions>(
            config.GetSection("Database"), strictness: OptionsStrictness.RequireSection));

        Assert.Equal(30, host.Services.GetRequiredService<IOptions<LenientOptions>>().Value.Timeout);
    }

    [Theory]
    [InlineData("""{ "Database": {} }""", false)]
    [InlineData("""{ "Database": null }""", false)]
    [InlineData("""{ "Database": { "Timeout": 30 } }""", true)]
    public void RequireSection_EmptyObjectAndNullCountAsMissing(string json, bool expectedValid)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();
        var services = new ServiceCollection();
        services.AddValidatedOptions<LenientOptions>(
            config.GetSection("Database"), strictness: OptionsStrictness.RequireSection);

        Exception? ex = Record.Exception(
            () => _ = services.BuildServiceProvider().GetRequiredService<IOptions<LenientOptions>>().Value);

        Assert.Equal(expectedValid, ex is null);
    }

    [Fact]
    public void RequireSection_RegisteredTwice_ReportsTheFailureOnce()
    {
        IConfiguration config = Memory();
        var services = new ServiceCollection();
        services.AddValidatedOptions<LenientOptions>(
            config.GetSection("Missing"), strictness: OptionsStrictness.RequireSection);
        services.AddValidatedOptions<LenientOptions>(
            config.GetSection("Missing"), strictness: OptionsStrictness.RequireSection);

        OptionsValidationException ex = Assert.Throws<OptionsValidationException>(
            () => _ = services.BuildServiceProvider().GetRequiredService<IOptions<LenientOptions>>().Value);

        Assert.Single(ex.Failures);
    }

    [Fact]
    public void RequireSection_IsScopedToTheNamedInstanceItWasRegisteredFor()
    {
        IConfiguration config = Memory(("Clients:Primary:Timeout", "30"));
        var services = new ServiceCollection();
        services.AddValidatedOptions<LenientOptions>(
            config.GetSection("Clients:Primary"), name: "primary", strictness: OptionsStrictness.RequireSection);
        services.AddValidatedOptions<LenientOptions>(
            config.GetSection("Clients:Secondary"), name: "secondary", strictness: OptionsStrictness.RequireSection);
        IOptionsMonitor<LenientOptions> monitor =
            services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<LenientOptions>>();

        Assert.Equal(30, monitor.Get("primary").Timeout);
        OptionsValidationException ex = Assert.Throws<OptionsValidationException>(() => monitor.Get("secondary"));
        Assert.Equal("secondary", ex.OptionsName);
        Assert.Single(ex.Failures);
    }

    [Fact]
    public void RequireSection_ReadsTheSectionLive_NotAtRegistration()
    {
        // The section is absent when registered and added before the first read, so a check that
        // captured Exists() at registration would fail here.
        var config = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddValidatedOptions<LenientOptions>(
            config.GetSection("Database"), strictness: OptionsStrictness.RequireSection);

        config.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Timeout"] = "30" });

        Assert.Equal(
            30,
            services.BuildServiceProvider().GetRequiredService<IOptions<LenientOptions>>().Value.Timeout);
    }

    // ---- RejectUnknownKeys ----

    [Fact]
    public async Task RejectUnknownKeys_MisspelledKey_FailsStartupNamingTheKey()
    {
        IConfiguration config = Memory(("Database:Timeuot", "30"));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartedHost(s => s.AddValidatedOptions<LenientOptions>(
                config.GetSection("Database"), strictness: OptionsStrictness.RejectUnknownKeys)));

        Assert.Contains("'Timeuot'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectUnknownKeys_AppliesToNestedObjects()
    {
        IConfiguration config = Memory(("Database:Retry:Attemtps", "3"));
        var services = new ServiceCollection();
        services.AddValidatedOptions<LenientOptions>(
            config.GetSection("Database"), strictness: OptionsStrictness.RejectUnknownKeys);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => _ = services.BuildServiceProvider().GetRequiredService<IOptions<LenientOptions>>().Value);

        Assert.Contains("'Attemtps'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectUnknownKeys_AcceptsKeysDifferingOnlyByCase_AndDictionaryKeys()
    {
        IConfiguration config = Memory(
            ("Database:timeout", "30"),
            ("Database:Tags:anything-at-all", "x"));
        var services = new ServiceCollection();
        services.AddValidatedOptions<LenientOptions>(
            config.GetSection("Database"), strictness: OptionsStrictness.RejectUnknownKeys);

        LenientOptions value = services.BuildServiceProvider().GetRequiredService<IOptions<LenientOptions>>().Value;

        Assert.Equal(30, value.Timeout);
        Assert.Equal("x", value.Tags["anything-at-all"]);
    }

    // ---- Both flags, forwarded by every overload ----

    public static TheoryData<string> Overloads => new()
    {
        "section",
        "configuration",
        "section+validator",
        "configuration+validator",
    };

    [Theory]
    [MemberData(nameof(Overloads))]
    public void EveryOverload_ForwardsRequireSection(string overload)
    {
        var services = new ServiceCollection();
        Register(services, overload, Memory(), OptionsStrictness.RequireSection);

        Assert.Throws<OptionsValidationException>(
            () => _ = services.BuildServiceProvider().GetRequiredService<IOptions<BoundOptions>>().Value);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void EveryOverload_ForwardsRejectUnknownKeys(string overload)
    {
        var services = new ServiceCollection();
        Register(
            services,
            overload,
            Memory(("Tests:Strict:Timeout", "30"), ("Tests:Strict:Unknown", "x")),
            OptionsStrictness.RejectUnknownKeys);

        Assert.Throws<InvalidOperationException>(
            () => _ = services.BuildServiceProvider().GetRequiredService<IOptions<BoundOptions>>().Value);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void EveryOverload_WithBothFlags_AcceptsCorrectConfiguration(string overload)
    {
        var services = new ServiceCollection();
        Register(
            services,
            overload,
            Memory(("Tests:Strict:Timeout", "30")),
            OptionsStrictness.RequireSection | OptionsStrictness.RejectUnknownKeys);

        Assert.Equal(
            30,
            services.BuildServiceProvider().GetRequiredService<IOptions<BoundOptions>>().Value.Timeout);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void EveryOverload_RejectsAnUndefinedFlag(string overload)
    {
        var services = new ServiceCollection();

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => Register(services, overload, Memory(), (OptionsStrictness)4));

        Assert.Equal("strictness", ex.ParamName);
    }

    private static void Register(
        IServiceCollection services, string overload, IConfiguration config, OptionsStrictness strictness)
    {
        switch (overload)
        {
            case "section":
                services.AddValidatedOptions<BoundOptions>(
                    config.GetSection(BoundOptions.SectionName), strictness: strictness);
                break;
            case "configuration":
                services.AddValidatedOptions<BoundOptions>(config, strictness: strictness);
                break;
            case "section+validator":
                services.AddValidatedOptions<BoundOptions, BoundOptionsValidator>(
                    config.GetSection(BoundOptions.SectionName), strictness: strictness);
                break;
            case "configuration+validator":
                services.AddValidatedOptions<BoundOptions, BoundOptionsValidator>(config, strictness: strictness);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(overload), overload, null);
        }
    }

    private static IConfiguration Memory(params (string Key, string? Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static async Task<IHost> StartedHost(Action<IServiceCollection> register)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        register(builder.Services);
        IHost host = builder.Build();
        try
        {
            await host.StartAsync();
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>Every property optional, so only the strictness checks can reject it.</summary>
    public sealed class LenientOptions
    {
        public int Timeout { get; set; } = 10;

        public RetryOptions Retry { get; set; } = new();

        public Dictionary<string, string> Tags { get; set; } = [];
    }

    public sealed class RetryOptions
    {
        public int Attempts { get; set; }
    }

    public sealed class BoundOptions : ISectionBoundOptions
    {
        public static string SectionName => "Tests:Strict";

        [Range(0, 300)]
        public int Timeout { get; set; }
    }

    [OptionsValidator]
    public sealed partial class BoundOptionsValidator : IValidateOptions<BoundOptions>
    {
    }
}
