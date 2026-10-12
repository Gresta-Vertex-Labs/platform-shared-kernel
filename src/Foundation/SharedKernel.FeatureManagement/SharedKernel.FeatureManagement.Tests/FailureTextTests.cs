using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using Xunit;
using ErrorType = OpenFeature.Constant.ErrorType;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>
/// No exception text and no configuration value reaches an evaluation message, a startup line or a log record
/// (rule 27). A sentinel value is planted in every failing shape.
/// </summary>
public sealed class FailureTextTests
{
    private const string Sentinel = "s3cr3t-sentinel";
    private const string OverflowingNumber = "99999999999";

    private static readonly FeatureFlag<CheckoutSettings?> Probe =
        FeatureFlag.Object<CheckoutSettings?>("Probe", null, TestJsonContext.Default.CheckoutSettings);

    private static string Configuration(string configurationValueJson) => $$"""
        {
          "feature_management": {
            "feature_flags": [
              {
                "id": "Probe",
                "enabled": true,
                "variants": [ { "name": "Bad", "configuration_value": {{configurationValueJson}} } ],
                "allocation": { "default_when_enabled": "Bad" }
              }
            ]
          }
        }
        """;

    private static string Settings(string steps = "\"3\"", string express = "\"true\"", string title = "\"t\"", string providers = "[\"card\"]") =>
        $$"""{ "steps": {{steps}}, "expressPay": {{express}}, "title": {{title}}, "providers": {{providers}}, "code": "c" }""";

    public static TheoryData<string, string, string> BadVariants => new()
    {
        { $"\"{Sentinel}\"", "an object was expected", Sentinel },
        { Settings(express: $"\"{Sentinel}\""), "a value is not true or false", Sentinel },
        { Settings(steps: $"\"{Sentinel}\""), "a value is not a number", Sentinel },
        { Settings(providers: $"\"{Sentinel}\""), "a list was expected", Sentinel },
        { Settings(title: $$"""{ "inner": "{{Sentinel}}" }"""), "a single value was expected, not an object or a list", Sentinel },
        { Settings(steps: $"\"{OverflowingNumber}\""), "the value does not fit the flag's type", OverflowingNumber },
    };

    [Theory]
    [MemberData(nameof(BadVariants))]
    public async Task Evaluation_OverABadVariant_ReportsAFixedReason_WithoutTheValue(string variant, string reason, string sentinel)
    {
        var logs = new InMemoryLoggerFactory();
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(Configuration(variant)),
            before: s => s.AddSingleton<ILoggerFactory>(logs));

        FlagEvaluationDetails<CheckoutSettings?> details = await provider.NewScopeClient().GetDetailsAsync(Probe);

        Assert.Null(details.Value);
        Assert.Equal(ErrorType.ParseError, details.ErrorType);
        Assert.Equal("Bad", details.Variant);
        Assert.Equal($"Variant 'Bad' does not fit CheckoutSettings: {reason}.", details.ErrorMessage);
        Assert.DoesNotContain(sentinel, details.ErrorMessage, StringComparison.Ordinal);
        Records(logs).ShouldNotLeak(sentinel);
    }

    [Theory]
    [MemberData(nameof(BadVariants))]
    public async Task StartupValidation_OverABadVariant_ListsAFixedReason_WithoutTheValue(string variant, string reason, string sentinel)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddConfiguration(FeatureTestHost.Json(Configuration(variant)));
        builder.Services.AddSharedKernelFeatureManagement(builder.Configuration, o => o.ValidateOnStart(Probe));
        using IHost host = builder.Build();

        var error = await Assert.ThrowsAsync<FeatureFlagValidationException>(() => host.StartAsync());

        Assert.Equal([$"'Probe' variant 'Bad': {reason}."], error.Failures);
        Assert.DoesNotContain(sentinel, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluation_OverAVariantNestedTooDeeply_ReturnsADefault_WithoutTheValue()
    {
        // The JSON configuration reader refuses depth beyond 64, so build the deep section from flat keys.
        string deepKey = string.Join(':', Enumerable.Repeat("a", 70));
        const string flag = "feature_management:feature_flags:0";
        var configuration = FeatureTestHost.InMemory(
            ($"{flag}:id", "Probe"),
            ($"{flag}:enabled", "true"),
            ($"{flag}:variants:0:name", "Bad"),
            ($"{flag}:variants:0:configuration_value:{deepKey}", Sentinel),
            ($"{flag}:allocation:default_when_enabled", "Bad"));

        var logs = new InMemoryLoggerFactory();
        await using var provider = await FeatureTestHost.StartAsync(
            configuration,
            before: s => s.AddSingleton<ILoggerFactory>(logs));

        FlagEvaluationDetails<CheckoutSettings?> details = await provider.NewScopeClient().GetDetailsAsync(Probe);

        Assert.Null(details.Value);
        Assert.NotEqual(ErrorType.None, details.ErrorType);
        Assert.DoesNotContain(Sentinel, details.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Records(logs).ShouldNotLeak(Sentinel);
    }

    [FilterAlias("LeakyFilter")]
    private sealed class LeakyFilter : IFeatureFilter
    {
        public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context) =>
            throw new InvalidOperationException($"backend password is {Sentinel}");
    }

    [Fact]
    public async Task AThrowingProvider_ReportsOnlyTheExceptionTypeName_AndLogsOnceWithTheException()
    {
        const string configuration = """
            { "feature_management": { "feature_flags": [
              { "id": "Leaky", "enabled": true, "conditions": { "client_filters": [ { "name": "LeakyFilter" } ] } } ] } }
            """;
        var logs = new InMemoryLoggerFactory();
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(configuration),
            o => o.AddFeatureFilter<LeakyFilter>(),
            before: s => s.AddSingleton<ILoggerFactory>(logs));

        FlagEvaluationDetails<bool> details = await provider.NewScopeClient().GetDetailsAsync(FeatureFlag.Boolean("Leaky", true));

        Assert.True(details.Value);
        Assert.Equal(ErrorType.General, details.ErrorType);
        Assert.Equal("Flag 'Leaky' could not be evaluated (InvalidOperationException); the default value was used.", details.ErrorMessage);
        Assert.DoesNotContain(Sentinel, details.ErrorMessage, StringComparison.Ordinal);

        IReadOnlyList<LogRecord> records = Records(logs);
        records.ShouldHaveLoggedCount(new EventId(1301), 1);
        LogRecord record = records.ShouldHaveLogged(new EventId(1301));
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.DoesNotContain(Sentinel, record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, record.Template, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEvaluationFailureLog_FollowsTheLogConvention()
    {
        const string configuration = """
            { "feature_management": { "feature_flags": [
              { "id": "Leaky", "enabled": true, "conditions": { "client_filters": [ { "name": "LeakyFilter" } ] } } ] } }
            """;
        var logs = new InMemoryLoggerFactory();
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(configuration),
            o => o.AddFeatureFilter<LeakyFilter>(),
            before: s => s.AddSingleton<ILoggerFactory>(logs));

        await provider.NewScopeClient().GetDetailsAsync(FeatureFlag.Boolean("Leaky", true));

        // Category: the provider's ILogger<T> full name (L7); the provider type is internal, so the name is spelled out.
        // OpenFeature's own logger (a different category) logs through its own templates and is not this package's.
        const string category = "SharedKernel.FeatureManagement.Internal.MicrosoftFeatureManagementProvider";
        Assert.Contains(category, logs.Loggers.Keys);
        IReadOnlyList<LogRecord> records = logs.Loggers[category].Records;
        records.ShouldFollowLogConvention();
        records.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Core, 3);
        LogRecord record = records.ShouldHaveLogged(new EventId(1301), LogLevel.Error);
        Assert.Equal("Evaluating the feature flag '{FlagKey}' failed, so the caller's default value was used.", record.Template);
        Assert.True(record.TryGetProperty("FlagKey", out object? flagKey));
        Assert.Equal("Leaky", flagKey);
        Assert.IsType<InvalidOperationException>(record.Exception);
    }

    [Fact]
    public void FeatureManagementEventIds_ArePinnedLiterally()
    {
        Assert.Equal(1000, LoggingEventIdRanges.Core);
        Assert.Equal(1300, LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Core, 3));
        Assert.True(LoggingEventIdRanges.IsInSubBlock(1301, LoggingEventIdRanges.Core, 3));
    }

    [Fact]
    public void FeatureFlagValidationException_IsAnUnexpectedSharedKernelException()
    {
        var exception = new FeatureFlagValidationException(["'A' is not configured.", "'B' has no variants."]);

        Assert.IsAssignableFrom<SharedKernelException>(exception);
        Assert.IsAssignableFrom<IHasError>(exception);
        Assert.Equal("feature_management.invalid_flag_configuration", FeatureManagementErrorCodes.InvalidFlagConfiguration);
        Assert.Equal(FeatureManagementErrorCodes.InvalidFlagConfiguration, exception.Error.Code);
        Assert.Equal(SharedKernel.Primitives.Errors.ErrorType.Unexpected, exception.Error.Type);
        Assert.Equal(["'A' is not configured.", "'B' has no variants."], exception.Failures);
        Assert.Contains("- 'A' is not configured.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("- 'B' has no variants.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureFlagValidationException_ErrorConstructors_KeepTheErrorAndHaveNoFailures()
    {
        Error error = Error.Unexpected("custom.code", "Custom message.");
        var inner = new InvalidOperationException();

        var plain = new FeatureFlagValidationException(error);
        var wrapped = new FeatureFlagValidationException(error, inner);

        Assert.Same(error, plain.Error);
        Assert.Empty(plain.Failures);
        Assert.Same(inner, wrapped.InnerException);
        Assert.Empty(wrapped.Failures);
    }

    [Fact]
    public void FeatureFlagValidationException_RejectsNullFailures() =>
        Assert.Throws<ArgumentNullException>(() => new FeatureFlagValidationException((IReadOnlyList<string>)null!));

    private static IReadOnlyList<LogRecord> Records(InMemoryLoggerFactory logs) =>
        logs.Loggers.Values.SelectMany(static l => l.Records).ToList();
}
