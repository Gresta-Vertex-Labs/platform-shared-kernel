using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SharedKernel.Persistence.EfCore.Jsonb;

/// <summary>
/// EF Core fluent extension methods for configuring JSONB columns on PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// Configures an explicit <see cref="ValueConverter{TModel,TProvider}"/> (STJ string
/// serialization) plus a structural <see cref="ValueComparer{T}"/> — without the latter, EF Core's
/// default reference-equality comparer for a reference-typed property considers the property
/// "unchanged" on every <c>SaveChanges</c> unless the CLR reference itself was replaced, silently
/// dropping in-place mutations of the deserialized object graph (e.g.
/// <c>entity.Metadata.Tags.Add("x")</c> never persists).
/// </para>
/// <para>
/// <strong>Prefer EF Core 10's native <c>ComplexProperty(...).ToJson()</c> when the JSON payload's
/// shape is itself a mapped complex type</strong> (queryable/projectable members, real change
/// tracking with no hand-rolled comparer). This method remains the right choice for an opaque
/// payload the application never needs to query by sub-field, or when the CLR type is a plain
/// application DTO/dictionary not meant to become a first-class complex type.
/// </para>
/// </remarks>
#pragma warning disable RS0027 // API with optional parameter(s) should have the most parameters amongst its public overloads.
// HasJsonbColumn<TEntity,TProperty>'s two overloads both take exactly three parameters — the last is
// EITHER an optional JsonSerializerOptions? (reflection-based STJ serialization) OR a mandatory
// JsonTypeInfo<TProperty> (source-generated, AOT-safe serialization) — two mutually exclusive,
// non-convertible types at that position, so the argument a caller passes (or omits entirely, which
// only the JsonSerializerOptions? overload permits) already selects the correct overload; there is
// no shared call shape for a trailing optional parameter to ever disambiguate incorrectly.
public static class JsonbEntityTypeBuilderExtensions
{
    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.General);

    /// <summary>
    /// Configures the property identified by <paramref name="propertyExpression"/> to use
    /// the PostgreSQL <c>jsonb</c> column type, serialized with <paramref name="serializerOptions"/>
    /// (or a small platform default when omitted) and compared structurally.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <typeparam name="TProperty">The property type (typically a complex object serialized as JSON).</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="propertyExpression">An expression selecting the property to configure.</param>
    /// <param name="serializerOptions">
    /// Optional STJ options. When <see langword="null"/>, a small platform default
    /// (<see cref="JsonSerializerDefaults.General"/>) is used. Reflection-based — prefer the
    /// <see cref="JsonTypeInfo{T}"/> overload for a source-generated, AOT/trim-friendly path.
    /// </param>
    /// <returns>The underlying <see cref="PropertyBuilder{TProperty}"/> for further configuration.</returns>
    public static PropertyBuilder<TProperty> HasJsonbColumn<TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TProperty>> propertyExpression,
        JsonSerializerOptions? serializerOptions = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(propertyExpression);

        var options = serializerOptions ?? DefaultOptions;

        return Configure(
            builder.Property(propertyExpression),
            model => JsonSerializer.Serialize(model, options),
            provider => JsonSerializer.Deserialize<TProperty>(provider, options)!);
    }

    /// <summary>
    /// Configures the property identified by <paramref name="propertyExpression"/> to use
    /// the PostgreSQL <c>jsonb</c> column type, serialized with the source-generated
    /// <paramref name="typeInfo"/> — the reflection-free, AOT/trim-friendly path.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="propertyExpression">An expression selecting the property to configure.</param>
    /// <param name="typeInfo">
    /// The source-generated <see cref="JsonTypeInfo{T}"/> for <typeparamref name="TProperty"/> (a
    /// member of a <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>).
    /// </param>
    /// <returns>The underlying <see cref="PropertyBuilder{TProperty}"/> for further configuration.</returns>
    public static PropertyBuilder<TProperty> HasJsonbColumn<TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TProperty>> propertyExpression,
        JsonTypeInfo<TProperty> typeInfo)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(propertyExpression);
        ArgumentNullException.ThrowIfNull(typeInfo);

        return Configure(
            builder.Property(propertyExpression),
            model => JsonSerializer.Serialize(model, typeInfo),
            provider => JsonSerializer.Deserialize(provider, typeInfo)!);
    }

    private static PropertyBuilder<TProperty> Configure<TProperty>(
        PropertyBuilder<TProperty> propertyBuilder,
        Func<TProperty, string> serialize,
        Func<string, TProperty> deserialize)
    {
        propertyBuilder.HasColumnType("jsonb");

        propertyBuilder.HasConversion(
            new ValueConverter<TProperty, string>(
                model => serialize(model),
                provider => deserialize(provider)));

        // Structural comparer: two values are equal iff their canonical JSON text is equal, and a
        // snapshot is a fresh deserialize of that text — this is what makes EF Core detect an
        // in-place mutation of the deserialized object graph as a real change, instead of the
        // default reference-equality comparer silently ignoring it.
        propertyBuilder.Metadata.SetValueComparer(
            new ValueComparer<TProperty>(
                (left, right) => JsonEquals(left, right, serialize),
                value => JsonHash(value, serialize),
                value => JsonSnapshot(value, serialize, deserialize)));

        return propertyBuilder;
    }

    // Each helper below is invoked from inside an Expression<Func<...>> that ValueComparer<T>'s
    // constructor compiles — a MethodCallExpression referencing a plain method is legal there; C#
    // pattern matching ('is'/'is not') directly inside the lambda body itself is not (CS8122), which
    // is why the null handling lives here rather than inline in the lambdas above.
    private static bool JsonEquals<TProperty>(TProperty? left, TProperty? right, Func<TProperty, string> serialize)
    {
        if (left is null)
            return right is null;

        return right is not null && string.Equals(serialize(left), serialize(right), StringComparison.Ordinal);
    }

    private static int JsonHash<TProperty>(TProperty? value, Func<TProperty, string> serialize) =>
        value is null ? 0 : serialize(value).GetHashCode(StringComparison.Ordinal);

    private static TProperty JsonSnapshot<TProperty>(
        TProperty? value,
        Func<TProperty, string> serialize,
        Func<string, TProperty> deserialize) =>
        value is null ? default! : deserialize(serialize(value));
}
#pragma warning restore RS0027
