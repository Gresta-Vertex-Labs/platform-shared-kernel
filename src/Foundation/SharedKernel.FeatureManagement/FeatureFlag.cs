using System.Globalization;
using System.Text.Json.Serialization.Metadata;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;
using SharedKernel.FeatureManagement.Internal;

namespace SharedKernel.FeatureManagement;

/// <summary>
/// A feature flag declared once in code: its key, its value type and the value callers get when the
/// flag is missing or cannot be evaluated. Declare flags as <c>static readonly</c> fields and pass them to
/// <see cref="FeatureClientExtensions.GetValueAsync{T}(IFeatureClient, FeatureFlag{T}, CancellationToken)"/>
/// instead of repeating the key as a string at every call site.
/// </summary>
/// <example>
/// <code>
/// public static class Flags
/// {
///     public static readonly FeatureFlag&lt;bool&gt; NewCheckout =
///         FeatureFlag.Boolean("NewCheckout", description: "The redesigned checkout flow.");
///
///     public static readonly FeatureFlag&lt;string&gt; CheckoutTheme =
///         FeatureFlag.String("CheckoutTheme", defaultValue: "classic");
/// }
/// </code>
/// </example>
public abstract class FeatureFlag
{
    private protected FeatureFlag(string key, FeatureFlagKind kind, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Kind = kind;
        Description = description;
    }

    /// <summary>The flag's key, exactly as it appears in configuration.</summary>
    public string Key { get; }

    /// <summary>The type of value the flag returns.</summary>
    public FeatureFlagKind Kind { get; }

    /// <summary>What the flag controls. Documentation only; never evaluated.</summary>
    public string? Description { get; }

    /// <summary>Returns <see cref="Key"/>.</summary>
    /// <returns>The flag's key.</returns>
    public override string ToString() => Key;

    /// <summary>
    /// Declares an on/off flag. It is on when the feature is enabled for the caller.
    /// </summary>
    /// <param name="key">The flag's key in configuration.</param>
    /// <param name="defaultValue">
    /// The value returned when the flag is missing or cannot be evaluated. Defaults to <see langword="false"/>,
    /// so an unknown or broken flag keeps the feature off.
    /// </param>
    /// <param name="description">What the flag controls.</param>
    /// <returns>The declared flag.</returns>
    public static FeatureFlag<bool> Boolean(string key, bool defaultValue = false, string? description = null) =>
        new(key, FeatureFlagKind.Boolean, defaultValue, description,
            static (client, flag, context, ct) => client.GetBooleanDetailsAsync(flag.Key, flag.DefaultValue, context, null, ct),
            static _ => null);

    /// <summary>
    /// Declares a flag whose assigned variant carries a text value (<c>configuration_value</c> in configuration).
    /// </summary>
    /// <param name="key">The flag's key in configuration.</param>
    /// <param name="defaultValue">The value returned when no variant is assigned or the flag cannot be evaluated.</param>
    /// <param name="description">What the flag controls.</param>
    /// <returns>The declared flag.</returns>
    public static FeatureFlag<string> String(string key, string defaultValue, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        return new(key, FeatureFlagKind.String, defaultValue, description,
            static (client, flag, context, ct) => client.GetStringDetailsAsync(flag.Key, flag.DefaultValue, context, null, ct),
            static configuration => VariantValues.TryGetString(configuration, out _, out string? error) ? null : error);
    }

    /// <summary>
    /// Declares a flag whose assigned variant carries a whole number (<c>configuration_value</c>, invariant culture).
    /// </summary>
    /// <param name="key">The flag's key in configuration.</param>
    /// <param name="defaultValue">The value returned when no variant is assigned or the flag cannot be evaluated.</param>
    /// <param name="description">What the flag controls.</param>
    /// <returns>The declared flag.</returns>
    public static FeatureFlag<int> Integer(string key, int defaultValue, string? description = null) =>
        new(key, FeatureFlagKind.Integer, defaultValue, description,
            static (client, flag, context, ct) => client.GetIntegerDetailsAsync(flag.Key, flag.DefaultValue, context, null, ct),
            static configuration => VariantValues.TryGetInteger(configuration, out _, out string? error) ? null : error);

    /// <summary>
    /// Declares a flag whose assigned variant carries a number (<c>configuration_value</c>, invariant culture).
    /// </summary>
    /// <param name="key">The flag's key in configuration.</param>
    /// <param name="defaultValue">The value returned when no variant is assigned or the flag cannot be evaluated.</param>
    /// <param name="description">What the flag controls.</param>
    /// <returns>The declared flag.</returns>
    public static FeatureFlag<double> Double(string key, double defaultValue, string? description = null) =>
        new(key, FeatureFlagKind.Double, defaultValue, description,
            static (client, flag, context, ct) => client.GetDoubleDetailsAsync(flag.Key, flag.DefaultValue, context, null, ct),
            static configuration => VariantValues.TryGetDouble(configuration, out _, out string? error) ? null : error);

    /// <summary>
    /// Declares a flag whose assigned variant carries a JSON object, read into <typeparamref name="T"/> through
    /// a source-generated <see cref="JsonTypeInfo{T}"/> (no reflection).
    /// </summary>
    /// <remarks>
    /// Configuration stores every value as text, so each value is converted to the type of the property it
    /// fills: <c>"true"</c> to a <see cref="bool"/>, <c>"42"</c> to an <see cref="int"/>, <c>"42"</c> to a
    /// <see cref="string"/> when the property is a string. A value that does not fit its property makes the
    /// evaluation return <paramref name="defaultValue"/> with <see cref="ErrorType.ParseError"/>, and fails
    /// startup validation.
    /// </remarks>
    /// <typeparam name="T">The type the variant's configuration is read into.</typeparam>
    /// <param name="key">The flag's key in configuration.</param>
    /// <param name="defaultValue">The value returned when no variant is assigned or the flag cannot be evaluated.</param>
    /// <param name="typeInfo">Source-generated metadata for <typeparamref name="T"/>, from a <c>JsonSerializerContext</c>.</param>
    /// <param name="description">What the flag controls.</param>
    /// <returns>The declared flag.</returns>
    public static FeatureFlag<T> Object<T>(string key, T defaultValue, JsonTypeInfo<T> typeInfo, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return new(key, FeatureFlagKind.Object, defaultValue, description,
            (client, flag, context, ct) => EvaluateObjectAsync(client, flag, typeInfo, context, ct),
            configuration => TypedValueConverter.TryConvert(ConfigurationValues.ToValue(configuration), typeInfo, out _, out string? error)
                ? null
                : error);
    }

    /// <summary>
    /// Checks a variant's configuration against this flag's type. Returns <see langword="null"/> when it fits.
    /// </summary>
    internal abstract string? CheckVariant(Microsoft.Extensions.Configuration.IConfigurationSection? configuration);

    private static async Task<FlagEvaluationDetails<T>> EvaluateObjectAsync<T>(
        IFeatureClient client,
        FeatureFlag<T> flag,
        JsonTypeInfo<T> typeInfo,
        EvaluationContext? context,
        CancellationToken cancellationToken)
    {
        FlagEvaluationDetails<Value> details = await client
            .GetObjectDetailsAsync(flag.Key, new Value(), context, null, cancellationToken)
            .ConfigureAwait(false);

        if (details.ErrorType != ErrorType.None || details.Value is null || details.Value.IsNull)
        {
            return new FlagEvaluationDetails<T>(
                flag.Key, flag.DefaultValue, details.ErrorType, details.Reason, details.Variant, details.ErrorMessage, details.FlagMetadata);
        }

        if (TypedValueConverter.TryConvert(details.Value, typeInfo, out T? value, out string? error))
        {
            return new FlagEvaluationDetails<T>(
                flag.Key, value!, ErrorType.None, details.Reason, details.Variant, null, details.FlagMetadata);
        }

        return new FlagEvaluationDetails<T>(
            flag.Key,
            flag.DefaultValue,
            ErrorType.ParseError,
            Reason.Error,
            details.Variant,
            string.Create(CultureInfo.InvariantCulture, $"Variant '{details.Variant}' does not fit {typeof(T).Name}: {error}"),
            details.FlagMetadata);
    }
}

/// <summary>
/// A feature flag that returns a <typeparamref name="T"/>. Create one with the factory methods on
/// <see cref="FeatureFlag"/>: <see cref="FeatureFlag.Boolean"/>, <see cref="FeatureFlag.String"/>,
/// <see cref="FeatureFlag.Integer"/>, <see cref="FeatureFlag.Double"/> or <see cref="FeatureFlag.Object{T}"/>.
/// </summary>
/// <typeparam name="T">The flag's value type.</typeparam>
public sealed class FeatureFlag<T> : FeatureFlag
{
    private readonly Func<IFeatureClient, FeatureFlag<T>, EvaluationContext?, CancellationToken, Task<FlagEvaluationDetails<T>>> _evaluate;
    private readonly Func<Microsoft.Extensions.Configuration.IConfigurationSection?, string?> _checkVariant;

    internal FeatureFlag(
        string key,
        FeatureFlagKind kind,
        T defaultValue,
        string? description,
        Func<IFeatureClient, FeatureFlag<T>, EvaluationContext?, CancellationToken, Task<FlagEvaluationDetails<T>>> evaluate,
        Func<Microsoft.Extensions.Configuration.IConfigurationSection?, string?> checkVariant)
        : base(key, kind, description)
    {
        DefaultValue = defaultValue;
        _evaluate = evaluate;
        _checkVariant = checkVariant;
    }

    /// <summary>The value returned when the flag is missing, has no assigned variant, or cannot be evaluated.</summary>
    public T DefaultValue { get; }

    internal Task<FlagEvaluationDetails<T>> EvaluateAsync(
        IFeatureClient client,
        EvaluationContext? context,
        CancellationToken cancellationToken) =>
        _evaluate(client, this, context, cancellationToken);

    internal override string? CheckVariant(Microsoft.Extensions.Configuration.IConfigurationSection? configuration) =>
        _checkVariant(configuration);
}

/// <summary>The type of value a <see cref="FeatureFlag"/> returns.</summary>
public enum FeatureFlagKind
{
    /// <summary>On or off: whether the feature is enabled for the caller.</summary>
    Boolean,

    /// <summary>The assigned variant's text value.</summary>
    String,

    /// <summary>The assigned variant's whole-number value.</summary>
    Integer,

    /// <summary>The assigned variant's numeric value.</summary>
    Double,

    /// <summary>The assigned variant's JSON object, read into a typed value.</summary>
    Object,
}
