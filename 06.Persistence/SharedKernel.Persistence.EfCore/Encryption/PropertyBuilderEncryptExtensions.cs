using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Extension methods on <see cref="PropertyBuilder{TProperty}"/> for marking EF Core string
/// properties as encrypted using the <c>SharedKernel:Encrypt</c> annotation.
/// </summary>
/// <remarks>
/// This is the <strong>only</strong> permitted way to mark a property for field-level encryption
/// (SK0304). Do not instantiate <see cref="EncryptedValueConverter{T}"/> directly in
/// <c>IEntityTypeConfiguration</c> implementations — <see cref="EncryptionModelConvention"/>
/// applies the converter automatically during model finalization.
/// </remarks>
public static class PropertyBuilderEncryptExtensions
{
    internal const string AnnotationKey = "SharedKernel:Encrypt";

    /// <summary>
    /// Marks this string property for transparent field-level AES-256-GCM encryption.
    /// </summary>
    /// <typeparam name="TProperty">The property CLR type (typically <see langword="string"/>).</typeparam>
    /// <param name="builder">The property builder to annotate.</param>
    /// <param name="enabled">
    /// <see langword="true"/> (default) to opt in; <see langword="false"/> to explicitly opt out;
    /// <see langword="null"/> is treated as <see langword="true"/>.
    /// </param>
    /// <returns>The same property builder for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Property(x => x.Email).HasMaxLength(255).Encrypt().IsRequired();
    /// builder.Property(x => x.PhoneNumber).HasMaxLength(20).Encrypt().IsRequired();
    /// </code>
    /// </example>
    public static PropertyBuilder<TProperty> Encrypt<TProperty>(
        this PropertyBuilder<TProperty> builder,
        bool? enabled = true)
    {
        builder.HasAnnotation(AnnotationKey, enabled ?? true);
        return builder;
    }
}
