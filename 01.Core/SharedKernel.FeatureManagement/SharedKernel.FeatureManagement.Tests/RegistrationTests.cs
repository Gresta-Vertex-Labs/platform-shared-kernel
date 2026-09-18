using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>Registration options, custom filters, hooks and the never-throw guarantee.</summary>
public sealed class RegistrationTests
{
    private const string Configuration = """
        {
          "feature_management": {
            "feature_flags": [
              { "id": "Exploding", "enabled": true, "conditions": { "client_filters": [ { "name": "Explodes" } ] } },
              { "id": "WeekendOnly", "enabled": true, "conditions": { "client_filters": [ { "name": "Weekend", "parameters": { "Allowed": true } } ] } }
            ]
          }
        }
        """;

    [FilterAlias("Explodes")]
    private sealed class ExplodingFilter : IFeatureFilter
    {
        public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context) =>
            throw new InvalidOperationException("The flag service is down.");
    }

    [FilterAlias("Weekend")]
    private sealed class WeekendFilter : IFeatureFilter
    {
        public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context) =>
            Task.FromResult(context.Parameters["Allowed"] == "True");
    }

    private sealed class CountingHook : Hook
    {
        public int Calls;

        public override ValueTask FinallyAsync<T>(HookContext<T> context, FlagEvaluationDetails<T> evaluationDetails, IReadOnlyDictionary<string, object>? hints = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return ValueTask.CompletedTask;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailingFilter_ReturnsTheDefault_WithGeneralError_AndLogsAWarning(bool defaultValue)
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(Configuration),
            o => o.AddFeatureFilter<ExplodingFilter>(),
            before: s => s.AddLogging(b => b.AddProvider(logs)));

        FlagEvaluationDetails<bool> details = await provider.NewScopeClient()
            .GetDetailsAsync(FeatureFlag.Boolean("Exploding", defaultValue));

        Assert.Equal(defaultValue, details.Value);
        Assert.Equal(ErrorType.General, details.ErrorType);
        Assert.DoesNotContain("service is down", details.ErrorMessage, StringComparison.Ordinal);
        var entry = Assert.Single(logs.Entries, e => e.EventId.Id == 1301);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("Feature flag Exploding could not be evaluated; the caller's default value was used", entry.Message);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task ACustomFilter_IsUsedByConfiguration()
    {
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(Configuration), o => o.AddFeatureFilter<WeekendFilter>());

        Assert.True(await provider.NewScopeClient().IsEnabledAsync(FeatureFlag.Boolean("WeekendOnly")));
    }

    [Fact]
    public async Task ConfigureOpenFeature_AddsHooks()
    {
        var hook = new CountingHook();
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(Configuration), o => o.ConfigureOpenFeature(b => b.AddHook(hook)));

        await provider.NewScopeClient().IsEnabledAsync(FeatureFlag.Boolean("Anything"));

        Assert.Equal(1, hook.Calls);
    }

    [Fact]
    public void CallingTwice_Throws()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelFeatureManagement(FeatureTestHost.Json("{}"));

        var error = Assert.Throws<InvalidOperationException>(() => services.AddSharedKernelFeatureManagement(FeatureTestHost.Json("{}")));
        Assert.Contains("already been called", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonPositiveScopeResultLifetime_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection()
            .AddSharedKernelFeatureManagement(FeatureTestHost.Json("{}"), o => o.ScopeResultLifetime = TimeSpan.Zero));

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddSharedKernelFeatureManagement(FeatureTestHost.Json("{}")));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddSharedKernelFeatureManagement(null!));
    }

    [Fact]
    public void ValidateOnStart_IgnoresARepeatedKey()
    {
        var options = new FeatureFlagOptions();
        options.ValidateOnStart(FeatureFlag.Boolean("A"), FeatureFlag.Boolean("B")).ValidateOnStart(FeatureFlag.Boolean("A"));

        Assert.Equal(["A", "B"], options.FlagsToValidate.Select(f => f.Key));
    }

    [Fact]
    public async Task BeforeTheProviderIsReady_EvaluationsReturnTheDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelFeatureManagement(FeatureTestHost.Json(BooleanFlagTests.Configuration));
        await using ServiceProvider provider = services.BuildServiceProvider();

        FlagEvaluationDetails<bool> details = await provider.NewScopeClient().GetDetailsAsync(FeatureFlag.Boolean("AlwaysOn"));

        Assert.False(details.Value);
        Assert.Equal(ErrorType.ProviderNotReady, details.ErrorType);
    }
}
