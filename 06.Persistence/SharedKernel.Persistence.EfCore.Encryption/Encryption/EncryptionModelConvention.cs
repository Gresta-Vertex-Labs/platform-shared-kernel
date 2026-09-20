using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SharedKernel.Persistence.EfCore.Encryption.Rotation;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// EF Core model-finalizing convention that validates every <c>.Encrypt(...)</c>-annotated property and, for one
/// that also called <c>.WithBlindIndex()</c>, adds its shadow blind-index column.
/// </summary>
/// <remarks>
/// <para>
/// Registered automatically by <c>EfCorePersistenceBuilder{TContext}.WithEncryption()</c>, via
/// <see cref="EncryptionModelConventionFactory"/>. Never construct it directly (SK0304's replacement guard covers
/// this type by name).
/// </para>
/// <para>
/// <strong>No <c>ValueConverter</c> here.</strong> Unlike the platform's other property-level annotations, this
/// convention does not wire a <c>ValueConverter</c> — a value converter can only see the ONE property being
/// converted, never the row's primary key or tenant id, both of which the associated-data binding requires (see
/// <see cref="AssociatedDataBuilder"/>). Encryption and decryption instead happen in <see cref="EncryptionInterceptor"/>,
/// which has entity-graph access. This convention's job is purely: fail fast on an unsupported shape, and add the
/// blind-index shadow property when requested.
/// </para>
/// <para>
/// <strong>Marks properties as consumed:</strong> after successfully processing a property, this convention sets
/// <see cref="PersistenceModelAnnotationNames.EncryptApplied"/> alongside the original
/// <see cref="PersistenceModelAnnotationNames.Encrypt"/> annotation (left in place — <see cref="EncryptionInterceptor"/>
/// reads it at runtime). <see cref="Conventions.EncryptAnnotationRegisteredGuardConvention"/>, registered
/// unconditionally by the core package, fails model building for any property that still lacks the "applied"
/// marker after every contributed convention has run — closing the "annotated but encryption never wired" gap.
/// </para>
/// </remarks>
public sealed class EncryptionModelConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        // ToList(): adding a shadow property below mutates the entity type's property collection while we would
        // otherwise still be enumerating it.
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            var validatedRotationKeyShape = false;

            void EnsureRotationKeyShapeValidated()
            {
                if (validatedRotationKeyShape)
                    return;

                // Fail at model build, not only when a rotation happens to run — an entity with an encrypted
                // property but a key shape IEncryptionRotationJob cannot rotate must never reach production
                // unnoticed. Validated once per entity type (idempotent — cheap to repeat, but no need to).
                ValidateRotationKeyShape(entityType);
                validatedRotationKeyShape = true;
            }

            foreach (var property in entityType.GetProperties().ToList())
            {
                var purposeAnnotation = property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt);
                if (purposeAnnotation?.Value is not string purpose)
                    continue;

                if (property.ClrType != typeof(string))
                {
                    throw new InvalidOperationException(
                        $"'{entityType.ShortName()}.{property.Name}' calls '.Encrypt(\"{purpose}\")' but its CLR " +
                        $"type is '{property.ClrType.Name}', not 'string' — only 'string' properties are " +
                        "supported by field-level encryption.");
                }

                EnsureRotationKeyShapeValidated();

                if (property.FindAnnotation(PropertyBuilderEncryptExtensions.BlindIndexAnnotationKey)?.Value is true)
                {
                    AddBlindIndexShadowProperty(entityType, property.Name);
                }

                property.Builder.HasAnnotation(PersistenceModelAnnotationNames.EncryptApplied, true);
            }

            // EF Core 10 complex-type (value-object) properties are NOT included in GetProperties() above — they
            // live in their own IConventionComplexType, with no table/primary key of their own (their columns are
            // flattened onto the OWNING entity type's own table). '.Encrypt(...)' on a complex sub-property sets
            // the identical annotation (PropertyBuilder<TProperty> is the same static type either way), so only
            // the TRAVERSAL needs to be taught about this shape — validation/marking/blind-index below is
            // otherwise identical, just rooted at the complex type's properties and the OWNING entity type's key.
            foreach (var complexProperty in entityType.GetComplexProperties().ToList())
            {
                foreach (var property in complexProperty.ComplexType.GetProperties().ToList())
                {
                    var purposeAnnotation = property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt);
                    if (purposeAnnotation?.Value is not string purpose)
                        continue;

                    if (property.ClrType != typeof(string))
                    {
                        throw new InvalidOperationException(
                            $"'{entityType.ShortName()}.{complexProperty.Name}.{property.Name}' calls " +
                            $"'.Encrypt(\"{purpose}\")' but its CLR type is '{property.ClrType.Name}', not " +
                            "'string' — only 'string' properties are supported by field-level encryption.");
                    }

                    EnsureRotationKeyShapeValidated();

                    if (property.FindAnnotation(PropertyBuilderEncryptExtensions.BlindIndexAnnotationKey)?.Value is true)
                    {
                        // Qualified with the complex property's own name: two different complex properties of the
                        // same complex type (e.g. two addresses) must not collide on one shadow column.
                        AddBlindIndexShadowProperty(entityType, $"{complexProperty.Name}_{property.Name}");
                    }

                    property.Builder.HasAnnotation(PersistenceModelAnnotationNames.EncryptApplied, true);
                }
            }
        }
    }

    // Mirrors the shape EncryptionRotationService<TContext>.BuildTargets actually rotates — see
    // RotationKeySupport.Classify's remarks for exactly which shapes are supported and why.
    private static void ValidateRotationKeyShape(IConventionEntityType entityType)
    {
        var key = entityType.FindPrimaryKey();
        if (key is null)
        {
            throw new InvalidOperationException(
                $"'{entityType.ShortName()}' has an '.Encrypt(...)' property but no primary key — field-level " +
                "encryption needs one to bind its associated data to the owning row.");
        }

        if (key.Properties.Count != 1)
        {
            throw new InvalidOperationException(
                $"'{entityType.ShortName()}' has an '.Encrypt(...)' property and a composite primary key — " +
                "IEncryptionRotationJob supports only single-column primary keys. Remove the composite key, or " +
                "remove '.Encrypt(...)' from this entity type.");
        }

        var pkProperty = key.Properties[0];

        // A same-table owned entity type's primary key is ALWAYS a shadow property in EF Core 10 (mirroring its
        // owner's key, whether pinned via a.Property<T>("Id")/a.HasKey("Id") or left to the default owned-type
        // convention) — it has no backing CLR field or auto-property, so nothing stores its value on a bare
        // materialized instance. AssociatedDataBuilder needs the primary key at materialization time, from the
        // instance alone (IMaterializationInterceptor has no EntityEntry/shadow-value-buffer access) — genuinely
        // unreadable for a shadow key, not merely inconvenient. Encrypt the property on the OWNING entity type
        // instead, or promote the owned type's key to a real CLR property (not a shadow one).
        if (pkProperty.IsShadowProperty())
        {
            throw new InvalidOperationException(
                $"'{entityType.ShortName()}' has an '.Encrypt(...)' property but its primary key " +
                $"('{pkProperty.Name}') is a shadow property with no CLR-backed storage — field-level encryption " +
                "cannot read it from a materialized instance to compute associated data. This is the case for " +
                "every same-table owned entity type's key. Move the '.Encrypt(...)' property to the owning " +
                "entity type instead, or give this type a real (non-shadow) CLR primary key property.");
        }

        var providerClrType = pkProperty.GetValueConverter()?.ProviderClrType ?? pkProperty.ClrType;

        if (RotationKeySupport.Classify(providerClrType) is null)
        {
            throw new InvalidOperationException(
                $"'{entityType.ShortName()}' has an '.Encrypt(...)' property but its primary key's provider type " +
                $"is '{providerClrType.Name}' — IEncryptionRotationJob supports only Guid, long, int and string " +
                "primary keys. Remove '.Encrypt(...)' from this entity type, or change its key shape.");
        }
    }

    // shadowPropertyNameBase is property.Name for a direct entity property, or "{ComplexPropertyName}_{property.Name}"
    // for a property inside a complex type — always added on entityType, since a complex type has no table of its
    // own to add a shadow property to.
    private static void AddBlindIndexShadowProperty(IConventionEntityType entityType, string shadowPropertyNameBase)
    {
        var shadowPropertyName = shadowPropertyNameBase + "BlindIndex";

        if (entityType.FindProperty(shadowPropertyName) is not null)
            return;

        var shadowProperty = entityType.Builder.Property(typeof(string), shadowPropertyName)
            ?? throw new InvalidOperationException(
                $"Could not add the blind-index shadow property '{shadowPropertyName}' to " +
                $"'{entityType.ShortName()}' — a property with that name may already exist with an incompatible shape.");

        shadowProperty.IsRequired(false);
    }
}
