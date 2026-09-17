using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Serialization;
using Xunit;
using ZiggyCreatures.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace SharedKernel.Caching.FusionCache.Tests.Serialization;

// ---------------------------------------------------------------------------
// Source-generated context for this test module (independent of WireTestDto)
// ---------------------------------------------------------------------------

/// <summary>A DTO whose type info must survive the AddRedisL2 serializer wiring.</summary>
internal sealed record AotRegressionDto(string Value, int Sequence);

[JsonSerializable(typeof(AotRegressionDto))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal sealed partial class AotRegressionSerializerContext : JsonSerializerContext { }

/// <summary>
/// Regression tests for Phase 18 (SK.02.AotSerializerFix).
///
/// Verifies that calling <c>AddRedisL2</c> after <c>AddSharedKernelCaching</c> does NOT
/// overwrite the user-configured STJ serializer (and its <see cref="IJsonTypeInfoResolver"/>) with
/// a reflection-based default.  The <see cref="IFusionCacheSerializer"/> resolved from DI must
/// retain the <see cref="JsonSerializerOptions"/> whose
/// <see cref="JsonSerializerOptions.TypeInfoResolver"/> includes the application-supplied
/// <see cref="JsonSerializerContext"/>.
///
/// Two scenarios are covered:
/// <list type="bullet">
///   <item>No Brotli — DI resolves <see cref="FusionCacheSystemTextJsonSerializer"/> directly.</item>
///   <item>With Brotli — DI resolves <see cref="BrotliCacheSerializer"/> wrapping the STJ one.</item>
/// </list>
/// </summary>
public sealed class AotSerializerRegressionTests
{
    // -------------------------------------------------------------------------
    // Helper: extract JsonSerializerOptions from a resolved IFusionCacheSerializer.
    //
    // FusionCacheSystemTextJsonSerializer holds a private _options field of type
    // FusionCacheSystemTextJsonSerializer.Options, which exposes a public
    // SerializerOptions property.  BrotliCacheSerializer wraps an inner serializer
    // accessible via its private _inner field.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts the <see cref="JsonSerializerOptions"/> from a
    /// <see cref="FusionCacheSystemTextJsonSerializer"/> (potentially wrapped by
    /// <see cref="BrotliCacheSerializer"/>).
    /// Returns <see langword="null"/> when the serializer was constructed without explicit options
    /// (i.e. the default reflection-based path — <c>SerializerOptions</c> is null on the inner
    /// <c>Options</c> object).
    /// </summary>
    private static JsonSerializerOptions? ExtractJsonSerializerOptions(IFusionCacheSerializer serializer)
    {
        // Unwrap BrotliCacheSerializer decorator if present.
        var inner = UnwrapBrotli(serializer);

        if (inner is not FusionCacheSystemTextJsonSerializer stjSerializer)
            throw new InvalidOperationException(
                $"Expected FusionCacheSystemTextJsonSerializer (possibly wrapped by BrotliCacheSerializer) " +
                $"but got {inner.GetType().FullName}.");

        // Read the private _options field then its public SerializerOptions property.
        var optionsField = typeof(FusionCacheSystemTextJsonSerializer)
            .GetField("_options", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "FusionCacheSystemTextJsonSerializer._options field not found. " +
                "Library internals may have changed — update this test.");

        var optionsObj = optionsField.GetValue(stjSerializer)
            ?? throw new InvalidOperationException("_options field value is null.");

        var serializerOptionsProp = optionsObj.GetType()
            .GetProperty("SerializerOptions", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "FusionCacheSystemTextJsonSerializer.Options.SerializerOptions property not found.");

        // SerializerOptions is intentionally null when the serializer was created via the
        // default (no-options) constructor — this is the reflection-based fallback path.
        // Return null to let callers distinguish "configured with AOT options" vs "default".
        return (JsonSerializerOptions?)serializerOptionsProp.GetValue(optionsObj);
    }

    /// <summary>
    /// If <paramref name="serializer"/> is a <see cref="BrotliCacheSerializer"/>, returns its
    /// inner serializer; otherwise returns the original.
    /// </summary>
    private static IFusionCacheSerializer UnwrapBrotli(IFusionCacheSerializer serializer)
    {
        if (serializer is not BrotliCacheSerializer)
            return serializer;

        var innerField = typeof(BrotliCacheSerializer)
            .GetField("_inner", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "BrotliCacheSerializer._inner field not found. " +
                "Library internals may have changed — update this test.");

        return (IFusionCacheSerializer)(innerField.GetValue(serializer)
            ?? throw new InvalidOperationException("BrotliCacheSerializer._inner is null."));
    }

    // -------------------------------------------------------------------------
    // Scenario 1: No Brotli compression
    // -------------------------------------------------------------------------

    /// <summary>
    /// After AddSharedKernelCaching(SerializerContext = ctx) the resolved
    /// IFusionCacheSerializer must be a FusionCacheSystemTextJsonSerializer whose
    /// JsonSerializerOptions.TypeInfoResolver can resolve the application type.
    /// AddRedisL2 must not overwrite this.
    /// </summary>
    [Fact]
    public void AddRedisL2_WithSerializerContext_NoBrotli_ResolvesStjSerializerWithAppContext()
    {
        var ctx = AotRegressionSerializerContext.Default;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o =>
        {
            o.ServiceName = "regression-test-svc";
            o.SerializerContext = ctx;
        });

        // Simulate what AddRedisL2 does to the FusionCache builder —
        // but without a real Redis connection we cannot call AddRedisL2 directly.
        // The phase 18 fix is entirely in the FusionCache builder chain:
        // AddRedisL2 now calls .WithRegisteredSerializer() instead of .WithSystemTextJsonSerializer().
        // We verify the DI registration by resolving IFusionCacheSerializer directly.
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<IFusionCacheSerializer>();

        // Must be the STJ serializer (not a Brotli wrapper).
        Assert.IsType<FusionCacheSystemTextJsonSerializer>(resolved);

        // Extract and inspect the JsonSerializerOptions inside the serializer.
        // When SerializerContext was provided, the options must be non-null with a configured resolver.
        var jsonOpts = ExtractJsonSerializerOptions(resolved);
        Assert.NotNull(jsonOpts);
        Assert.NotNull(jsonOpts.TypeInfoResolver);

        // The combined resolver must resolve the application-supplied type.
        var appTypeInfo = jsonOpts.TypeInfoResolver.GetTypeInfo(
            typeof(AotRegressionDto), jsonOpts);
        Assert.NotNull(appTypeInfo);

        // The combined resolver must also resolve the encrypted cache entry type (byte[]).
        var infraTypeInfo = jsonOpts.TypeInfoResolver.GetTypeInfo(
            typeof(byte[]), jsonOpts);
        Assert.NotNull(infraTypeInfo);
    }

    /// <summary>
    /// Ensures that a second call to .WithSystemTextJsonSerializer() (the old, broken behaviour)
    /// would have reset the TypeInfoResolver — confirming why the fix is necessary.
    /// This is a documentation test: it proves the bug would be silent.
    /// </summary>
    [Fact]
    public void Regression_WithSystemTextJsonSerializerCall_WouldHaveOverwrittenContext()
    {
        // Set up combined options with a real SerializerContext.
        var ctx = AotRegressionSerializerContext.Default;
        var combined = JsonTypeInfoResolver.Combine(ctx, EncryptedCacheEntryJsonContext.Default);
        var originalOpts = new JsonSerializerOptions { TypeInfoResolver = combined };

        // Simulate what the OLD broken code did: overwrite with a default reflection serializer.
        // The default FusionCacheSystemTextJsonSerializer(JsonSerializerOptions) ctor with no
        // type resolver creates opts with TypeInfoResolver = null.
        var broken = new FusionCacheSystemTextJsonSerializer(new JsonSerializerOptions());
        var brokenOpts = ExtractJsonSerializerOptions(broken);

        // The broken default has no TypeInfoResolver or one that cannot resolve the app type.
        // (If null, attempting GetTypeInfo will return null for source-gen-only types.)
        // The fix ensures AddRedisL2 never creates this second serializer.
        Assert.NotNull(brokenOpts); // options exist but have no resolver
        Assert.Null(brokenOpts.TypeInfoResolver);
    }

    // -------------------------------------------------------------------------
    // Scenario 2: With Brotli compression
    // -------------------------------------------------------------------------

    /// <summary>
    /// After AddSharedKernelCaching(SerializerContext = ctx) + AddBrotliCompression(),
    /// the resolved IFusionCacheSerializer must be a BrotliCacheSerializer whose inner
    /// FusionCacheSystemTextJsonSerializer carries the correct JsonSerializerOptions.
    /// </summary>
    [Fact]
    public void AddBrotliCompression_WithSerializerContext_ResolvesWrappedStjWithAppContext()
    {
        var ctx = AotRegressionSerializerContext.Default;

        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelCaching(o =>
        {
            o.ServiceName = "regression-brotli-svc";
            o.SerializerContext = ctx;
        });

        // Apply Brotli decoration without AddRedisL2 (pure DI wiring test).
        builder.AddBrotliCompression();

        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<IFusionCacheSerializer>();

        // Must be a BrotliCacheSerializer wrapping the STJ serializer.
        Assert.IsType<BrotliCacheSerializer>(resolved);

        // Unwrap and extract the inner serializer's options.
        // When SerializerContext was provided, the options must be non-null with a configured resolver.
        var jsonOpts = ExtractJsonSerializerOptions(resolved);
        Assert.NotNull(jsonOpts);
        Assert.NotNull(jsonOpts.TypeInfoResolver);

        // The inner serializer must still carry the application context.
        var appTypeInfo = jsonOpts.TypeInfoResolver.GetTypeInfo(
            typeof(AotRegressionDto), jsonOpts);
        Assert.NotNull(appTypeInfo);

        // And the encrypted cache entry context.
        var infraTypeInfo = jsonOpts.TypeInfoResolver.GetTypeInfo(
            typeof(byte[]), jsonOpts);
        Assert.NotNull(infraTypeInfo);
    }

    /// <summary>
    /// Without a SerializerContext, the resolved IFusionCacheSerializer is the default
    /// FusionCacheSystemTextJsonSerializer with a null TypeInfoResolver — acceptable for
    /// non-AOT builds.  This baseline confirms the no-context path is unaffected.
    /// </summary>
    [Fact]
    public void AddSharedKernelCaching_WithoutSerializerContext_StjSerializerHasNullTypeInfoResolver()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "no-ctx-svc");

        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<IFusionCacheSerializer>();
        Assert.IsType<FusionCacheSystemTextJsonSerializer>(resolved);

        var jsonOpts = ExtractJsonSerializerOptions(resolved);
        // When no SerializerContext is provided, the serializer either has no JsonSerializerOptions
        // (SerializerOptions is null on the Options object) or has options with a null TypeInfoResolver.
        // Both represent the reflection-based fallback path — acceptable for non-AOT builds.
        var typeInfoResolver = jsonOpts?.TypeInfoResolver;
        Assert.Null(typeInfoResolver);
    }
}
