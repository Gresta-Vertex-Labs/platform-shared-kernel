using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Encryption.Metadata;

/// <summary>
/// Validates every <c>.Encrypt(...)</c> property when the model is finalized, sizes its column, adds its blind-index
/// shadow property, and marks it as wired.
/// </summary>
/// <remarks>
/// <para>
/// Walks properties at every depth with <see cref="PersistenceModelAnnotationNames.GetPropertiesIncludingComplex"/>
/// (the traversal the interceptor, query guard and maintenance job also use) and rejects, at model build rather
/// than at the first save, every shape the rest of the pipeline cannot handle: a non-<see langword="string"/>
/// property, a property inside a complex collection, a JSON-mapped complex type or a struct complex type, an entity
/// without a single-column, client-readable primary key of a supported type, and a purpose used by two columns.
/// </para>
/// <para>
/// <strong>Purposes are unique across the model.</strong> The associated data binds purpose, primary key and
/// tenant, never the table, so two columns sharing a purpose could swap ciphertext undetected (between two tables
/// with overlapping integer keys, for example). The one exception is a TPH hierarchy whose sibling types map
/// properties to the same column: that is one column, so it keeps one purpose.
/// </para>
/// <para>
/// <strong>Column size:</strong> a declared maximum length is a plaintext length; the stored ciphertext is longer
/// (key id, nonce, tag, base64). The column is resized to the largest payload that plaintext length can produce.
/// Without a declared maximum the column stays unbounded (<c>text</c>).
/// </para>
/// </remarks>
internal sealed class EncryptionModelConvention : IModelFinalizingConvention
{
    /// <summary>The largest overhead of a stored payload over its UTF-8 plaintext, in bytes: version, key id length and a 255-byte key id, nonce and tag.</summary>
    private const int PayloadOverheadBytes = 2 + 255 + 12 + 16;

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var validated = new HashSet<IReadOnlyProperty>(ReferenceEqualityComparer.Instance);
        var purposes = new Dictionary<string, (IReadOnlyProperty Property, string DisplayName, string? Table, string? Column)>(StringComparer.Ordinal);

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            var keyShapeValidated = false;

            foreach (var (complexPath, property) in PersistenceModelAnnotationNames.GetPropertiesIncludingComplex(entityType).ToList())
            {
                if (property.FindAnnotation(EncryptionAnnotationNames.Purpose)?.Value is not string purpose)
                    continue;

                if (!keyShapeValidated)
                {
                    ValidateKeyShape(entityType);
                    keyShapeValidated = true;
                }

                if (!validated.Add(property))
                    continue; // An inherited property, already processed through the type that declares it.

                var path = string.Join('.', complexPath.Select(c => c.Name).Append(property.Name));
                var displayName = $"{entityType.ShortName()}.{path}";

                ValidateShape(property, complexPath, displayName, purpose);
                ValidatePurposeUnique(purposes, purpose, property, displayName, entityType);

                var conventionProperty = (IConventionProperty)property;
                SizeColumn(conventionProperty);

                if (property.FindAnnotation(EncryptionAnnotationNames.BlindIndexNormalization) is not null)
                {
                    var owner = complexPath.Count == 0
                        ? (IConventionEntityType)property.DeclaringType
                        : (IConventionEntityType)complexPath[0].DeclaringType;
                    var shadowName = path.Replace('.', '_') + "BlindIndex";
                    AddBlindIndexProperty(owner, shadowName, displayName);
                    conventionProperty.Builder.HasAnnotation(EncryptionAnnotationNames.BlindIndexProperty, shadowName);
                }

                conventionProperty.Builder.HasAnnotation(EncryptionAnnotationNames.Applied, true);
            }
        }
    }

    private static void ValidateShape(
        IReadOnlyProperty property, IReadOnlyList<IReadOnlyComplexProperty> complexPath, string displayName, string purpose)
    {
        if (property.ClrType != typeof(string))
        {
            throw new InvalidOperationException(
                $"'{displayName}' calls '.Encrypt(\"{purpose}\")' but its type is '{property.ClrType.Name}'. Only " +
                "'string' properties can be encrypted; store other values (numbers, dates, byte arrays) as a string " +
                "representation, or keep them unencrypted.");
        }

        foreach (var complexProperty in complexPath)
        {
            if (complexProperty.IsCollection)
            {
                throw new InvalidOperationException(
                    $"'{displayName}' is encrypted but lives in the complex collection '{complexProperty.Name}'. " +
                    "Complex collections are stored as JSON and cannot be encrypted per property; model the items as an " +
                    "entity type, or encrypt a property of the owning entity instead.");
            }

            if (complexProperty.ComplexType.IsMappedToJson())
            {
                throw new InvalidOperationException(
                    $"'{displayName}' is encrypted but '{complexProperty.Name}' is mapped to a JSON column. Encrypted " +
                    "properties must map to their own column; remove '.ToJson()' or the '.Encrypt(...)' call.");
            }

            if (complexProperty.ClrType.IsValueType)
            {
                throw new InvalidOperationException(
                    $"'{displayName}' is encrypted but '{complexProperty.Name}' is a struct complex type. Use a class " +
                    "for a complex type that contains encrypted properties.");
            }
        }
    }

    private static void ValidatePurposeUnique(
        Dictionary<string, (IReadOnlyProperty Property, string DisplayName, string? Table, string? Column)> purposes,
        string purpose,
        IReadOnlyProperty property,
        string displayName,
        IReadOnlyEntityType entityType)
    {
        var table = entityType.GetTableName();
        var column = property.GetColumnName();

        if (purposes.TryGetValue(purpose, out var first))
        {
            var sameColumn = table is not null && column is not null
                && string.Equals(first.Table, table, StringComparison.Ordinal)
                && string.Equals(first.Column, column, StringComparison.Ordinal);
            if (!sameColumn)
            {
                throw new InvalidOperationException(
                    $"'{displayName}' calls '.Encrypt(\"{purpose}\")', but '{first.DisplayName}' already uses that purpose. " +
                    "Every encrypted column needs its own purpose across the whole model: the associated data binds purpose, " +
                    "primary key and tenant, so two columns sharing a purpose could exchange ciphertext undetected.");
            }

            return;
        }

        purposes.Add(purpose, (property, displayName, table, column));
    }

    private static void SizeColumn(IConventionProperty property)
    {
        if (property.GetMaxLength() is not { } plaintextLength)
            return;

        var payloadBytes = PayloadOverheadBytes + (4L * plaintextLength);
        var storedLength = (int)Math.Min(int.MaxValue, ((payloadBytes * 4) + 2) / 3);
        ((IMutableProperty)property).SetMaxLength(storedLength);
    }

    private static void AddBlindIndexProperty(IConventionEntityType owner, string shadowName, string displayName)
    {
        if (owner.FindProperty(shadowName) is null)
        {
            var builder = owner.Builder.Property(typeof(string), shadowName)
                ?? throw new InvalidOperationException(
                    $"Could not add the blind-index property '{shadowName}' for '{displayName}'; a member with that name already exists.");
            builder.IsRequired(false);
            builder.HasMaxLength(BlindIndexer.MaxLength);
        }

        owner.Builder.HasIndex([shadowName]);
    }

    private static void ValidateKeyShape(IConventionEntityType entityType)
    {
        var key = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"'{entityType.ShortName()}' has an encrypted property but no primary key; the primary key is bound into every encrypted value.");

        if (key.Properties.Count != 1)
        {
            throw new InvalidOperationException(
                $"'{entityType.ShortName()}' has an encrypted property and a composite primary key; field encryption " +
                "supports single-column primary keys only.");
        }

        var keyProperty = key.Properties[0];
        if (keyProperty.IsShadowProperty())
        {
            throw new InvalidOperationException(
                $"'{entityType.ShortName()}' has an encrypted property but its primary key '{keyProperty.Name}' is a " +
                "shadow property (as every same-table owned type's is), so it cannot be read from a materialized " +
                "instance. Encrypt the property on the owning entity, or map it as a complex type.");
        }

        var providerType = keyProperty.GetValueConverter()?.ProviderClrType ?? keyProperty.ClrType;
        if (RotationKeySupport.Classify(providerType) is null)
        {
            throw new InvalidOperationException(
                $"'{entityType.ShortName()}' has an encrypted property but its primary key is stored as " +
                $"'{providerType.Name}'; field encryption supports Guid, long, int and string keys.");
        }
    }
}
