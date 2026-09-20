using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Extension methods on <see cref="PropertyBuilder{TProperty}"/> for marking EF Core <see langword="string"/>
/// properties as field-level AES-256-GCM encrypted.
/// </summary>
/// <remarks>
/// This is the <strong>only</strong> permitted way to mark a property for field-level encryption (SK0304). Do not
/// build an <c>EncryptedValueConverter</c>-style <c>ValueConverter</c> or a custom <c>ISaveChangesInterceptor</c>/
/// <c>IMaterializationInterceptor</c> pair by hand in an <c>IEntityTypeConfiguration</c> — this annotation is read
/// by <see cref="EncryptionModelConvention"/> and enforced by <see cref="EncryptionInterceptor"/>, both wired
/// automatically once <c>EfCorePersistenceBuilder{TContext}.WithEncryption()</c> has been called.
/// </remarks>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// Encrypt<TProperty> and WithBlindIndex<TProperty> each have a PropertyBuilder<TProperty> overload
// and a ComplexTypePropertyBuilder<TProperty> overload (the value-object counterpart) — these take
// mutually exclusive, INCOMPATIBLE first-parameter types, so a caller's own builder variable's
// static type already selects the correct overload; there is no shared call shape across the pair
// for a trailing optional parameter to ever disambiguate incorrectly.
public static partial class PropertyBuilderEncryptExtensions
{
    /// <summary>
    /// The model annotation key <see cref="Encrypt{TProperty}(PropertyBuilder{TProperty}, string, bool)"/> sets. Its value is the property's purpose label
    /// (a <see langword="string"/>), never a <see langword="bool"/> — every encrypted property must name its own
    /// stable purpose.
    /// </summary>
    /// <remarks>
    /// Shared, by exact string value, with <c>SharedKernel.Persistence.EfCore</c>'s own
    /// <c>PersistenceModelAnnotationNames.Encrypt</c> constant, which the core package's always-on
    /// <c>EncryptAnnotationRegisteredGuardConvention</c> reads to fail model building loudly when this annotation
    /// is present but <c>.WithEncryption()</c> was never called — see that convention's remarks for why the core
    /// package cannot reference this package's types directly and must instead share the bare string.
    /// </remarks>
    internal const string PurposeAnnotationKey = PersistenceModelAnnotationNames.Encrypt;

    /// <summary>The model annotation key <see cref="WithBlindIndex{TProperty}(PropertyBuilder{TProperty}, Func{string, string}?)"/> sets, a <see langword="bool"/>.</summary>
    internal const string BlindIndexAnnotationKey = "SharedKernel:Persistence:Encrypt:BlindIndex";

    /// <summary>
    /// The model annotation key holding the optional normalization delegate supplied to
    /// <see cref="WithBlindIndex{TProperty}(PropertyBuilder{TProperty}, Func{string, string}?)"/>, a <see cref="Func{T,TResult}"/> of <see langword="string"/> to
    /// <see langword="string"/>.
    /// </summary>
    internal const string BlindIndexNormalizeAnnotationKey = "SharedKernel:Persistence:Encrypt:BlindIndex:Normalize";

    /// <summary>
    /// The model annotation key <see cref="Encrypt{TProperty}(PropertyBuilder{TProperty}, string, bool)"/> sets when <c>perTenantKey</c> is
    /// <see langword="true"/>, a <see langword="bool"/>.
    /// </summary>
    internal const string PerTenantKeyAnnotationKey = "SharedKernel:Persistence:Encrypt:PerTenantKey";

    /// <summary>A purpose label is at most this many UTF-8 bytes — it is folded into every AAD computation.</summary>
    public const int MaxPurposeLength = 200;

    // Underscores are allowed within a segment (this class's own XML doc example, "payment.card_number", needs
    // one) — only '.' separates segments, and a segment may never be empty (no leading/trailing/doubled dots).
    [GeneratedRegex(@"^[a-z0-9_]+(\.[a-z0-9_]+)*$", RegexOptions.Compiled)]
    private static partial Regex PurposePattern { get; }

    /// <summary>
    /// Marks this <see langword="string"/> property for transparent field-level AES-256-GCM encryption.
    /// </summary>
    /// <typeparam name="TProperty">
    /// The property CLR type. Only <see langword="string"/> is supported; every other type fails model building
    /// (see <see cref="EncryptionModelConvention"/>).
    /// </typeparam>
    /// <param name="builder">The property builder to annotate.</param>
    /// <param name="purpose">
    /// A short, stable, lowercase dotted label for what this property holds, e.g. <c>"customer.email"</c> or
    /// <c>"payment.card_number"</c>. Mandatory — there is no default. It becomes part of every row's authenticated
    /// associated data (AAD) alongside the row's primary key and, for a tenanted entity, its tenant id — never the
    /// physical table/column/schema name, so renaming the underlying column or table never breaks existing
    /// ciphertext. Renaming <paramref name="purpose"/> itself, by contrast, DOES — it is baked into the AAD exactly
    /// like the other two components. Treat it as part of the stored data's contract: pick it once, keep it.
    /// </param>
    /// <param name="perTenantKey">
    /// Controls KEY DERIVATION only — the AAD already binds the tenant id for every <see cref="SharedKernel.Domain.Abstractions.IHasTenant"/>
    /// entity's encrypted properties unconditionally (see <paramref name="purpose"/>), regardless of this flag.
    /// When <see langword="true"/> (default <see langword="false"/>) and the declaring entity implements
    /// <see cref="SharedKernel.Domain.Abstractions.IHasTenant"/>, every encrypt/decrypt call for this property
    /// additionally derives a per-tenant subkey (HKDF-SHA256 over the registered root key, purpose
    /// <c>"{purpose}:tenant"</c>, context the tenant id) via
    /// <see cref="SharedKernel.Cryptography.KeyDerivation.PurposeBoundKeyProviderExtensions.ForPurposeSynchronous"/>.
    /// This gives genuine cross-tenant key isolation (one tenant's ciphertext can never be decrypted by presenting
    /// another tenant's id, even by an operator holding the root key) — a real, cheap, useful property. It is
    /// <strong>not</strong> crypto-shredding: because the subkey is deterministically re-derivable from the still-live
    /// root key and the tenant id (public, not secret), there is no way to make one tenant's data unrecoverable
    /// without destroying every tenant's data. Genuine per-tenant erasure needs a per-tenant root key registered
    /// under its own key id in the <c>IEncryptionKeyProvider</c>, deleted independently when that tenant is
    /// offboarded — a deployment/key-management policy this package does not (and cannot) automate.
    /// </param>
    /// <returns>The same property builder for fluent chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="purpose"/> is null, empty, or not lowercase dot-separated ASCII.</exception>
    /// <example>
    /// <code>
    /// builder.Property(x =&gt; x.Email).HasMaxLength(255).Encrypt("customer.email").WithBlindIndex().IsRequired();
    /// builder.Property(x =&gt; x.Ssn).HasMaxLength(20).Encrypt("customer.ssn", perTenantKey: true).IsRequired();
    /// </code>
    /// </example>
    public static PropertyBuilder<TProperty> Encrypt<TProperty>(
        this PropertyBuilder<TProperty> builder,
        string purpose,
        bool perTenantKey = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidatePurpose(purpose);

        builder.HasAnnotation(PurposeAnnotationKey, purpose);
        if (perTenantKey)
            builder.HasAnnotation(PerTenantKeyAnnotationKey, true);

        return builder;
    }

    /// <summary>
    /// Adds an HMAC-SHA256 blind-index shadow column so equality lookups against this encrypted property are
    /// possible without decrypting every row.
    /// </summary>
    /// <typeparam name="TProperty">The property CLR type.</typeparam>
    /// <param name="builder">The property builder — must already have called <see cref="Encrypt{TProperty}(PropertyBuilder{TProperty}, string, bool)"/>.</param>
    /// <param name="normalize">
    /// Optional normalization applied to the plaintext before hashing, e.g. <c>s =&gt; s.Trim().ToUpperInvariant()</c>
    /// for an email address, or <c>s =&gt; s.Replace(" ", "")</c> for an IBAN. Two values that normalize to the same
    /// text produce the same blind index and are therefore indistinguishable to an equality lookup — choose
    /// normalization that matches the equality semantics the property actually needs. Defaults to no normalization
    /// (exact byte-for-byte match required).
    /// </param>
    /// <returns>The same property builder for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="EncryptionModelConvention"/> adds a shadow <see langword="string"/> property named
    /// <c>"{PropertyName}BlindIndex"</c>, mapped to its own column, holding
    /// <c>HMAC-SHA256(normalize(plaintext), subkey)</c> as lowercase hex. <see cref="EncryptionInterceptor"/>
    /// computes and maintains it automatically on every insert/update — never set it directly.
    /// </para>
    /// <para>
    /// <strong>Key rotation story:</strong> the blind index's HMAC subkey is derived from whichever key
    /// <c>ISynchronousEncryptionKeyProvider.GetCurrentKey()</c> currently returns. When the root key rotates, rows
    /// not yet rotated keep a blind index computed under the OLD subkey; a fresh equality lookup (computed under
    /// the NEW subkey) will not match them until <c>IEncryptionRotationJob</c> re-encrypts — and, in the same
    /// pass, recomputes the blind index for — that row. Until a full rotation completes, an equality lookup can
    /// silently miss rows still on the previous key. Plan rotations with this window in mind.
    /// </para>
    /// <para>
    /// Use <c>SharedKernel.Persistence.EfCore.Encryption.BlindIndex.EncryptedPropertyQueryExtensions.WhereBlindIndexEquals</c>
    /// to query — never compare <c>EF.Property&lt;string&gt;(x, "...BlindIndex")</c> by hand, since that bypasses
    /// normalization and per-tenant-key derivation.
    /// </para>
    /// </remarks>
    public static PropertyBuilder<TProperty> WithBlindIndex<TProperty>(
        this PropertyBuilder<TProperty> builder,
        Func<string, string>? normalize = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasAnnotation(BlindIndexAnnotationKey, true);
        if (normalize is not null)
            builder.HasAnnotation(BlindIndexNormalizeAnnotationKey, normalize);

        return builder;
    }

    /// <summary>
    /// Marks this <see langword="string"/> property, inside an EF Core 10 complex type (value object), for
    /// transparent field-level AES-256-GCM encryption. The complex-type counterpart of
    /// <see cref="Encrypt{TProperty}(PropertyBuilder{TProperty}, string, bool)"/> — EF Core represents a complex
    /// type's own properties with a distinct builder type (<see cref="ComplexTypePropertyBuilder{TProperty}"/>,
    /// not <see cref="PropertyBuilder{TProperty}"/>), so this overload exists purely to reach it; every other
    /// parameter and the resulting annotation are identical.
    /// </summary>
    /// <typeparam name="TProperty">The property CLR type. Only <see langword="string"/> is supported.</typeparam>
    /// <param name="builder">The complex-type property builder to annotate.</param>
    /// <param name="purpose">See <see cref="Encrypt{TProperty}(PropertyBuilder{TProperty}, string, bool)"/>.</param>
    /// <param name="perTenantKey">See <see cref="Encrypt{TProperty}(PropertyBuilder{TProperty}, string, bool)"/>.</param>
    /// <returns>The same property builder for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// builder.ComplexProperty(x =&gt; x.BillingAddress, a =&gt;
    /// {
    /// a.Property(addr =&gt; addr.Line1).HasMaxLength(256).Encrypt("customer.billing_address.line1");
    /// });
    /// </code>
    /// </example>
    public static ComplexTypePropertyBuilder<TProperty> Encrypt<TProperty>(
        this ComplexTypePropertyBuilder<TProperty> builder,
        string purpose,
        bool perTenantKey = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidatePurpose(purpose);

        builder.HasAnnotation(PurposeAnnotationKey, purpose);
        if (perTenantKey)
            builder.HasAnnotation(PerTenantKeyAnnotationKey, true);

        return builder;
    }

    /// <summary>
    /// The complex-type counterpart of <see cref="WithBlindIndex{TProperty}(PropertyBuilder{TProperty}, Func{string, string}?)"/>
    /// — see that method's remarks. The shadow blind-index column is added to the OWNING entity type (a complex
    /// type has no table of its own), named <c>"{ComplexPropertyName}_{PropertyName}BlindIndex"</c>.
    /// </summary>
    /// <typeparam name="TProperty">The property CLR type.</typeparam>
    /// <param name="builder">The complex-type property builder — must already have called <see cref="Encrypt{TProperty}(ComplexTypePropertyBuilder{TProperty}, string, bool)"/>.</param>
    /// <param name="normalize">See <see cref="WithBlindIndex{TProperty}(PropertyBuilder{TProperty}, Func{string, string}?)"/>.</param>
    /// <returns>The same property builder for fluent chaining.</returns>
    public static ComplexTypePropertyBuilder<TProperty> WithBlindIndex<TProperty>(
        this ComplexTypePropertyBuilder<TProperty> builder,
        Func<string, string>? normalize = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasAnnotation(BlindIndexAnnotationKey, true);
        if (normalize is not null)
            builder.HasAnnotation(BlindIndexNormalizeAnnotationKey, normalize);

        return builder;
    }

    private static void ValidatePurpose(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        if (System.Text.Encoding.UTF8.GetByteCount(purpose) > MaxPurposeLength)
        {
            throw new ArgumentException(
                $"'{nameof(purpose)}' must be at most {MaxPurposeLength} UTF-8 bytes.", nameof(purpose));
        }

        if (!PurposePattern.IsMatch(purpose))
        {
            throw new ArgumentException(
                $"'{nameof(purpose)}' ('{purpose}') must be lowercase ASCII letters, digits and dot-separated " +
                "segments only, e.g. 'customer.email'.",
                nameof(purpose));
        }
    }
}
#pragma warning restore RS0026
