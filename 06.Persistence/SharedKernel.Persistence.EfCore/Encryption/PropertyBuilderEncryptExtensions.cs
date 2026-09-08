using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Extension methods on <see cref="PropertyBuilder{TProperty}"/> for marking EF Core string
/// properties as encrypted using the <c>SharedKernel:Encrypt</c> annotation.
/// </summary>
/// <remarks>
/// This is the <strong>only</strong> permitted way to mark a property for field-level encryption
/// (SK0304). Do not instantiate <see cref="EncryptedValueConverter"/> directly in
/// <c>IEntityTypeConfiguration</c> implementations — <see cref="EncryptionModelConvention"/>
/// applies the converter automatically during model finalization.
/// </remarks>
public static class PropertyBuilderEncryptExtensions
{
    internal const string AnnotationKey = "SharedKernel:Encrypt";

    /// <summary>
    /// Annotation key holding an explicit associated-data (AAD) override string, when supplied via
    /// the <c>associatedDataOverride</c> parameter (P-491/D-128/WO-081).
    /// </summary>
    internal const string AssociatedDataOverrideAnnotationKey = "SharedKernel:EncryptAssociatedDataOverride";

    /// <summary>
    /// Marks this string property for transparent field-level AES-256-GCM encryption.
    /// </summary>
    /// <typeparam name="TProperty">The property CLR type (typically <see langword="string"/>).</typeparam>
    /// <param name="builder">The property builder to annotate.</param>
    /// <param name="enabled">
    /// <see langword="true"/> (default) to opt in; <see langword="false"/> to explicitly opt out;
    /// <see langword="null"/> is treated as <see langword="true"/>.
    /// </param>
    /// <param name="associatedDataOverride">
    /// Optional stable string used as this property's associated-authenticated-data (AAD) binding
    /// (P-491/D-128/WO-081) instead of the default table+column storage-identity derivation. Supply
    /// this BEFORE ever encrypting a row when you anticipate renaming the underlying table or
    /// column, so that rename stays safe — a converter built with the same
    /// <paramref name="associatedDataOverride"/> string decrypts correctly regardless of the
    /// physical table/column name at the time. Omitting this parameter (the default) binds AAD to
    /// the table+column identity as it exists at model-finalization time; renaming afterward makes
    /// every existing row's ciphertext for that column permanently undecryptable — see
    /// <see cref="EncryptedValueConverter"/>'s class remarks for the full rename hazard.
    /// </param>
    /// <returns>The same property builder for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Property(x => x.Email).HasMaxLength(255).Encrypt().IsRequired();
    /// builder.Property(x => x.PhoneNumber).HasMaxLength(20).Encrypt().IsRequired();
    /// builder.Property(x => x.Ssn).HasMaxLength(20).Encrypt(associatedDataOverride: "Customer.Ssn").IsRequired();
    /// </code>
    /// </example>
    public static PropertyBuilder<TProperty> Encrypt<TProperty>(
        this PropertyBuilder<TProperty> builder,
        bool? enabled = true,
        string? associatedDataOverride = null)
    {
        builder.HasAnnotation(AnnotationKey, enabled ?? true);

        if (associatedDataOverride is not null)
        {
            builder.HasAnnotation(AssociatedDataOverrideAnnotationKey, associatedDataOverride);
        }

        return builder;
    }
}
