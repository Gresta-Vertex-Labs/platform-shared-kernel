using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Core.Extensions;
using SharedKernel.FeatureManagement.Abstractions;
using SharedKernel.FeatureManagement.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Enums;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Consumer.Tests;

/// <summary>
/// Dependency-graph verification for the published NuGet packages.
/// Resolved via the local feed (nupkgs/) — not project references.
/// Confirms the Primitives, Core, Configuration, and FeatureManagement
/// packages resolve and compose correctly as a consumer would use them.
/// </summary>
public sealed class ConsumerDependencyGraphTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Primitives
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Primitives_Result_Success_ResolvedFromPackage()
    {
        Result<int> result = Result<int>.Success(99);

        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Value);
    }

    [Fact]
    public void Primitives_Result_Failure_ResolvedFromPackage()
    {
        Result<int> result = Result<int>.Failure(
            Error.NotFound("item.notfound", "Not found."));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public void Primitives_Error_FactoryMethods_ResolvedFromPackage()
    {
        Error validation   = Error.Validation("v.code", "Validation error.");
        Error notFound     = Error.NotFound("nf.code", "Not found.");
        Error conflict     = Error.Conflict("c.code", "Conflict.");
        Error unauthorized = Error.Unauthorized("u.code", "Unauthorized.");
        Error unexpected   = Error.Unexpected("x.code", "Unexpected.");

        Assert.Equal(ErrorType.Validation,   validation.Type);
        Assert.Equal(ErrorType.NotFound,     notFound.Type);
        Assert.Equal(ErrorType.Conflict,     conflict.Type);
        Assert.Equal(ErrorType.Unauthorized, unauthorized.Type);
        Assert.Equal(ErrorType.Unexpected,   unexpected.Type);
    }

    [Fact]
    public void Primitives_SystemClock_ResolvedFromPackage()
    {
        IClock clock = new SystemClock();

        Assert.True(clock.UtcNow <= DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.True(clock.Today <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));
    }

    [Fact]
    public void Primitives_ErrorCodes_ConstantsResolvedFromPackage()
    {
        Assert.NotEmpty(ErrorCodes.Validation.Required);
        Assert.NotEmpty(ErrorCodes.NotFound.Default);
        Assert.NotEmpty(ErrorCodes.Conflict.Default);
        Assert.NotEmpty(ErrorCodes.Unauthorized.Default);
    }

    [Fact]
    public void Primitives_SmartEnum_ResolvedFromPackage()
    {
        // Access a static member to ensure ConsumerStatus type initialisation runs
        // (which populates the shared SmartEnum<ConsumerStatus, int> list) before lookup.
        _ = ConsumerStatus.Active;

        ConsumerStatus status = ConsumerStatus.FromValue(1);

        Assert.Equal(ConsumerStatus.Active, status);
        Assert.Equal(2, ConsumerStatus.List.Count);
    }

    [Fact]
    public void Primitives_ValidationResult_ResolvedFromPackage()
    {
        SharedKernel.Primitives.Results.ValidationResult valid =
            SharedKernel.Primitives.Results.ValidationResult.Success();
        SharedKernel.Primitives.Results.ValidationResult invalid =
            SharedKernel.Primitives.Results.ValidationResult.Failure(
            [
                Error.Validation("f1", "Field 1 required."),
                Error.Validation("f2", "Field 2 required.")
            ]);

        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
        Assert.Equal(2, invalid.Errors.Count);
    }

    [Fact]
    public void Primitives_ValidationResultGeneric_ResolvedFromPackage()
    {
        ValidationResult<int> valid = ValidationResult<int>.Success(42);
        ValidationResult<int> invalid = ValidationResult<int>.Failure(
            [Error.Validation("required", "Value required.")]);

        Assert.True(valid.IsValid);
        Assert.Equal(42, valid.Value);
        Assert.False(invalid.IsValid);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Core — railway extensions, BCL helpers
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Core_RailwayExtensions_MapBindMatch_ResolvedFromPackage()
    {
        string output = Result<int>.Success(5)
            .Map(x => x * 2)
            .Bind(x => x > 0
                ? Result<string>.Success(x.ToString())
                : Result<string>.Failure(Error.Validation("neg", "Negative.")))
            .Match(
                onSuccess: s => $"ok:{s}",
                onFailure: e => $"err:{e.Code}");

        Assert.Equal("ok:10", output);
    }

    [Fact]
    public void Core_RailwayExtensions_FailurePath_PropagatesError()
    {
        Error original = Error.NotFound("x.notfound", "Not found.");

        string output = Result<int>.Failure(original)
            .Map(x => x * 2)
            .Match(
                onSuccess: _ => "should not reach",
                onFailure: e => e.Code);

        Assert.Equal("x.notfound", output);
    }

    [Fact]
    public void Core_StringExtensions_CaseConversions_ResolvedFromPackage()
    {
        Assert.Equal("my_property_name", "MyPropertyName".ToSnakeCase());
        Assert.Equal("myPropertyName",   "MyPropertyName".ToCamelCase());
        Assert.Equal("MyPropertyName",   "myPropertyName".ToPascalCase());
    }

    [Fact]
    public void Core_EnumerableExtensions_ToBatches_ResolvedFromPackage()
    {
        int[] source = [1, 2, 3, 4, 5];
        List<IEnumerable<int>> batches = [.. source.ToBatches(2)];

        Assert.Equal(3, batches.Count);
    }

    [Fact]
    public void Core_GuidExtensions_IsEmpty_ResolvedFromPackage()
    {
        Assert.True(Guid.Empty.IsEmpty());
        Assert.False(Guid.NewGuid().IsEmpty());
    }

    [Fact]
    public void Core_DateTimeOffsetExtensions_ToUnixMilliseconds_ResolvedFromPackage()
    {
        DateTimeOffset epoch = DateTimeOffset.UnixEpoch;
        long ms = epoch.ToUnixMilliseconds();

        Assert.Equal(0L, ms);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Configuration — AddValidatedOptions
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Configuration_AddValidatedOptions_ValidConfig_StartsSuccessfully()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Consumer:Name"] = "test-service"
                }))
            .ConfigureServices((ctx, services) =>
                services.AddValidatedOptions<ConsumerOptions>(
                    ctx.Configuration.GetSection("Consumer")))
            .Build();

        // Should not throw — config is valid.
        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task Configuration_AddValidatedOptions_InvalidConfig_ThrowsAtStartup()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
                // Missing required "Consumer:Name" — should fail on start.
                cfg.AddInMemoryCollection(new Dictionary<string, string?>()))
            .ConfigureServices((ctx, services) =>
                services.AddValidatedOptions<ConsumerOptions>(
                    ctx.Configuration.GetSection("Consumer")))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.FeatureManagement — IFeatureManager
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FeatureManagement_EnabledFlag_ReturnsTrue()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FeatureManagement:BetaFeature"] = "true"
                }))
            .ConfigureServices((ctx, services) =>
                services.AddSharedKernelFeatureManagement(ctx.Configuration))
            .Build();

        await host.StartAsync();

        IFeatureManager fm = host.Services.GetRequiredService<IFeatureManager>();
        bool enabled = await fm.IsEnabledAsync("BetaFeature", CancellationToken.None);

        Assert.True(enabled);

        await host.StopAsync();
    }

    [Fact]
    public async Task FeatureManagement_DisabledFlag_ReturnsFalse()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FeatureManagement:BetaFeature"] = "false"
                }))
            .ConfigureServices((ctx, services) =>
                services.AddSharedKernelFeatureManagement(ctx.Configuration))
            .Build();

        await host.StartAsync();

        IFeatureManager fm = host.Services.GetRequiredService<IFeatureManager>();
        bool enabled = await fm.IsEnabledAsync("BetaFeature", CancellationToken.None);

        Assert.False(enabled);

        await host.StopAsync();
    }
}

// ──────────────────────────────────────────────────────────────────────────
// Test-local helpers — SmartEnum and Options used only in this project
// ──────────────────────────────────────────────────────────────────────────

internal sealed class ConsumerStatus : SmartEnum<ConsumerStatus, int>
{
    public static readonly ConsumerStatus Active   = new("Active",   1);
    public static readonly ConsumerStatus Inactive = new("Inactive", 2);

    private ConsumerStatus(string name, int value) : base(name, value) { }
}

internal sealed class ConsumerOptions
{
    [Required]
    public string Name { get; init; } = "";
}
