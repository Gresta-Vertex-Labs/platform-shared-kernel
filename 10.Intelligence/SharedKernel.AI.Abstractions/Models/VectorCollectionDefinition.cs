using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Abstractions.Models;

/// <summary>
/// The neutral declaration of one vector collection — its embedding model identity, dimension,
/// distance metric, optional tenant field, and declared metadata fields.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the domain's sharpest-edge type</b> — <see cref="EmbeddingModelId"/> +
/// <see cref="Dimension"/> + <see cref="DistanceMetric"/> together are what every write and query
/// validates against before any I/O. <see cref="TenantField"/> placement here (not in provider
/// options) mirrors <c>09.Search</c>'s <c>SearchIndexDefinition.TenantField</c> exactly, for the
/// identical reason — a service may legitimately have one tenanted collection and one global one, and
/// per-collection placement is what lets the fail-closed guard arm itself exactly where it should.
/// </para>
/// </remarks>
public sealed record VectorCollectionDefinition
{
    /// <summary>Gets the collection name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the identifier of the embedding model this collection's vectors were produced by.</summary>
    public required string EmbeddingModelId { get; init; }

    /// <summary>Gets the vector dimension.</summary>
    public required int Dimension { get; init; }

    /// <summary>Gets the distance metric this collection compares vectors under.</summary>
    public required VectorDistanceMetric DistanceMetric { get; init; }

    /// <summary>
    /// Gets the tenant discriminator field name, or <see langword="null"/> for a
    /// single-tenant/global collection.
    /// </summary>
    public string? TenantField { get; init; }

    /// <summary>Gets the declared metadata fields.</summary>
    public IReadOnlyList<VectorFieldDefinition> Fields { get; init; } = [];

    /// <summary>
    /// Creates a <see cref="VectorCollectionDefinition"/> with the given values, validating structural
    /// invariants. For full control including <see cref="TenantField"/>, use
    /// <see cref="VectorCollectionDefinitionBuilder"/> instead.
    /// </summary>
    /// <returns>
    /// A failed <see cref="Result{T}"/> with <c>IntelligenceErrors.InvalidCollectionDefinition</c> when
    /// <paramref name="name"/>/<paramref name="embeddingModelId"/> is empty, <paramref name="dimension"/>
    /// is not positive, any field name is empty, or two fields share a name; otherwise a successful
    /// <see cref="Result{T}"/>.
    /// </returns>
    public static Result<VectorCollectionDefinition> Create(
        string name,
        string embeddingModelId,
        int dimension,
        VectorDistanceMetric metric,
        IReadOnlyList<VectorFieldDefinition> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var structuralError = ValidateStructure(name, embeddingModelId, dimension);
        if (structuralError is not null)
        {
            return Result<VectorCollectionDefinition>.Failure(structuralError);
        }

        var fieldError = ValidateFields(fields);
        if (fieldError is not null)
        {
            return Result<VectorCollectionDefinition>.Failure(fieldError);
        }

        return Result<VectorCollectionDefinition>.Success(new VectorCollectionDefinition
        {
            Name = name,
            EmbeddingModelId = embeddingModelId,
            Dimension = dimension,
            DistanceMetric = metric,
            Fields = fields,
        });
    }

    /// <summary>
    /// Validates the structural (non-field) invariants shared by <see cref="Create"/> and
    /// <see cref="VectorCollectionDefinitionBuilder.Build"/>. Returns <see langword="null"/> when
    /// valid.
    /// </summary>
    internal static Error? ValidateStructure(string name, string embeddingModelId, int dimension)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return IntelligenceErrors.InvalidCollectionDefinition("Collection name must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(embeddingModelId))
        {
            return IntelligenceErrors.InvalidCollectionDefinition("EmbeddingModelId must not be empty.");
        }

        if (dimension <= 0)
        {
            return IntelligenceErrors.InvalidCollectionDefinition("Dimension must be a positive integer.");
        }

        return null;
    }

    /// <summary>
    /// Validates that no <paramref name="fields"/> entry has an empty name and that no two entries
    /// share a name. Returns <see langword="null"/> when valid.
    /// </summary>
    internal static Error? ValidateFields(IReadOnlyList<VectorFieldDefinition> fields)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
            {
                return IntelligenceErrors.InvalidCollectionDefinition("Field name must not be empty.");
            }

            if (!seenNames.Add(field.Name))
            {
                return IntelligenceErrors.InvalidCollectionDefinition($"Duplicate field name '{field.Name}'.");
            }
        }

        return null;
    }

    /// <summary>
    /// Gets a stable fingerprint of this collection's schema — a SHA-256 hash, rendered as lowercase
    /// hex, over a deterministic canonical string built from <see cref="Name"/>,
    /// <see cref="EmbeddingModelId"/>, <see cref="Dimension"/>, <see cref="DistanceMetric"/>,
    /// <see cref="TenantField"/>, and every <see cref="Fields"/> entry sorted by
    /// <see cref="VectorFieldDefinition.Name"/> using <see cref="StringComparer.Ordinal"/>.
    /// </summary>
    /// <remarks>
    /// Ordinal sorting makes the value independent of declaration order; rendering
    /// <see cref="DistanceMetric"/> and <see cref="VectorFieldDefinition.Kind"/> as explicit
    /// <see cref="int"/>s makes it independent of enum member renames — identical technique to
    /// <c>09.Search</c>'s <c>SearchIndexDefinition.Fingerprint</c>, reused deliberately rather than
    /// reinvented. Written into provisioned-collection metadata so
    /// <c>IVectorCollectionProvisioner.ProbeAsync</c> can detect schema drift.
    /// </remarks>
    public string Fingerprint
    {
        get
        {
            var canonical = BuildCanonicalString();
            var bytes = Encoding.UTF8.GetBytes(canonical);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexStringLower(hash);
        }
    }

    private string BuildCanonicalString()
    {
        var builder = new StringBuilder();
        builder.Append(Name).Append('\n');
        builder.Append(EmbeddingModelId).Append('\n');
        builder.Append(Dimension.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append(((int)DistanceMetric).ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append(TenantField ?? string.Empty).Append('\n');

        foreach (var field in Fields.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            builder
                .Append(field.Name).Append('|')
                .Append((int)field.Kind).Append('|')
                .Append(field.Filterable ? '1' : '0')
                .Append('\n');
        }

        return builder.ToString();
    }
}
