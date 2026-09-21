using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Metadata;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>Marks <see langword="string"/> properties for field-level AES-256-GCM encryption.</summary>
/// <remarks>
/// The only supported way to encrypt a property (SK0304): never a value converter or a hand-written interceptor.
/// Works on entity properties and on complex-type properties at any depth. The model stores only strings, flags and
/// names, so <c>dotnet ef migrations add</c> and compiled models (<c>dotnet ef dbcontext optimize</c>) work.
/// Encryption must be wired with <c>UseFieldEncryption(...)</c>, or model building fails.
/// </remarks>
#pragma warning disable RS0026 // Each pair of overloads takes mutually exclusive builder types, never ambiguous.
public static partial class PropertyBuilderEncryptExtensions
{
    /// <summary>The longest purpose, in UTF-8 bytes.</summary>
    internal const int MaxPurposeLength = 200;

    [GeneratedRegex(@"^[a-z0-9_]+(\.[a-z0-9_]+)*$")]
    private static partial Regex PurposePattern { get; }

    /// <summary>Encrypts this <see langword="string"/> property.</summary>
    /// <typeparam name="TProperty">The property type; only <see langword="string"/> is supported.</typeparam>
    /// <param name="builder">The property builder.</param>
    /// <param name="purpose">
    /// A short, stable, lowercase dotted label for what the property holds, unique in the model, e.g.
    /// <c>"customer.email"</c>. It is bound into every stored value and selects the property's own derived key:
    /// renaming the table or column never breaks stored data, renaming the purpose does.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    /// A declared <c>HasMaxLength(n)</c> is the plaintext length; the column is resized to fit the ciphertext.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Property(x =&gt; x.Email).HasMaxLength(320).Encrypt("customer.email").WithBlindIndex(BlindIndexNormalization.Trim | BlindIndexNormalization.CaseFold);
    /// builder.Property(x =&gt; x.NationalId).Encrypt("customer.national_id");
    /// </code>
    /// </example>
    public static PropertyBuilder<TProperty> Encrypt<TProperty>(this PropertyBuilder<TProperty> builder, string purpose)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidatePurpose(purpose);
        return builder.HasAnnotation(EncryptionAnnotationNames.Purpose, purpose);
    }

    /// <summary>Encrypts this <see langword="string"/> property of a complex type. See <see cref="Encrypt{TProperty}(PropertyBuilder{TProperty}, string)"/>.</summary>
    /// <typeparam name="TProperty">The property type; only <see langword="string"/> is supported.</typeparam>
    /// <param name="builder">The complex-type property builder.</param>
    /// <param name="purpose">See <see cref="Encrypt{TProperty}(PropertyBuilder{TProperty}, string)"/>.</param>
    /// <returns>The same builder.</returns>
    public static ComplexTypePropertyBuilder<TProperty> Encrypt<TProperty>(this ComplexTypePropertyBuilder<TProperty> builder, string purpose)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidatePurpose(purpose);
        return builder.HasAnnotation(EncryptionAnnotationNames.Purpose, purpose);
    }

    /// <summary>
    /// Adds a blind index to this encrypted property, so it can be found by equality with
    /// <c>WhereEncryptedEquals(x =&gt; x.Property, value)</c>.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="builder">The property builder; call after <c>.Encrypt(...)</c>.</param>
    /// <param name="normalization">Built-in normalization applied before indexing, so equal values index equally.</param>
    /// <param name="normalizer">The name of a registered <see cref="IBlindIndexNormalizer"/> applied after <paramref name="normalization"/>.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    /// <para>
    /// The index is a keyed HMAC in a shadow column named after the property (<c>EmailBlindIndex</c>,
    /// <c>Billing_IbanBlindIndex</c>), maintained on every save; never set it yourself. It uses its own versioned
    /// key (<see cref="IBlindIndexKeyProvider"/>), so rotating the encryption key never affects lookups.
    /// </para>
    /// <para>
    /// An index reveals which rows hold equal values (within one tenant); do not add one to a low-cardinality
    /// property (a flag, a country) whose values could be guessed from their frequency.
    /// </para>
    /// </remarks>
    public static PropertyBuilder<TProperty> WithBlindIndex<TProperty>(
        this PropertyBuilder<TProperty> builder,
        BlindIndexNormalization normalization = BlindIndexNormalization.None,
        string? normalizer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasAnnotation(EncryptionAnnotationNames.BlindIndexNormalization, (int)normalization);
        if (normalizer is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(normalizer);
            builder.HasAnnotation(EncryptionAnnotationNames.BlindIndexNormalizer, normalizer);
        }

        return builder;
    }

    /// <summary>Adds a blind index to this encrypted complex-type property. See <see cref="WithBlindIndex{TProperty}(PropertyBuilder{TProperty}, BlindIndexNormalization, string?)"/>.</summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="builder">The complex-type property builder; call after <c>.Encrypt(...)</c>.</param>
    /// <param name="normalization">See <see cref="WithBlindIndex{TProperty}(PropertyBuilder{TProperty}, BlindIndexNormalization, string?)"/>.</param>
    /// <param name="normalizer">See <see cref="WithBlindIndex{TProperty}(PropertyBuilder{TProperty}, BlindIndexNormalization, string?)"/>.</param>
    /// <returns>The same builder.</returns>
    public static ComplexTypePropertyBuilder<TProperty> WithBlindIndex<TProperty>(
        this ComplexTypePropertyBuilder<TProperty> builder,
        BlindIndexNormalization normalization = BlindIndexNormalization.None,
        string? normalizer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasAnnotation(EncryptionAnnotationNames.BlindIndexNormalization, (int)normalization);
        if (normalizer is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(normalizer);
            builder.HasAnnotation(EncryptionAnnotationNames.BlindIndexNormalizer, normalizer);
        }

        return builder;
    }

    private static void ValidatePurpose(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        if (System.Text.Encoding.UTF8.GetByteCount(purpose) > MaxPurposeLength)
            throw new ArgumentException($"'{nameof(purpose)}' must be at most {MaxPurposeLength} UTF-8 bytes.", nameof(purpose));

        if (!PurposePattern.IsMatch(purpose))
        {
            throw new ArgumentException(
                $"'{nameof(purpose)}' ('{purpose}') must be lowercase ASCII letters, digits and underscores in " +
                "dot-separated segments, e.g. 'customer.email'.",
                nameof(purpose));
        }
    }
}
#pragma warning restore RS0026
