using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Abstractions.Models;

/// <summary>
/// A fluent builder for <see cref="VectorCollectionDefinition"/> — the ergonomic entry point
/// providers' <c>AddCollection&lt;TRecord&gt;</c> DI extensions expose to a composition root.
/// </summary>
public sealed class VectorCollectionDefinitionBuilder
{
    private readonly string _name;
    private readonly List<VectorFieldDefinition> _fields = [];
    private string? _embeddingModelId;
    private int? _dimension;
    private VectorDistanceMetric? _metric;
    private string? _tenantField;

    /// <summary>Initializes a new <see cref="VectorCollectionDefinitionBuilder"/> for the given collection name.</summary>
    public VectorCollectionDefinitionBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    /// <summary>Sets the embedding model identity and vector dimension.</summary>
    public VectorCollectionDefinitionBuilder EmbeddingModel(string modelId, int dimension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _embeddingModelId = modelId;
        _dimension = dimension;
        return this;
    }

    /// <summary>Sets the distance metric.</summary>
    public VectorCollectionDefinitionBuilder DistanceMetric(VectorDistanceMetric metric)
    {
        _metric = metric;
        return this;
    }

    /// <summary>Sets the tenant discriminator field name.</summary>
    public VectorCollectionDefinitionBuilder TenantField(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        _tenantField = field;
        return this;
    }

    /// <summary>Declares a metadata field.</summary>
    public VectorCollectionDefinitionBuilder Field(string name, VectorFieldKind kind, bool filterable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _fields.Add(new VectorFieldDefinition
        {
            Name = name,
            Kind = kind,
            Filterable = filterable,
        });
        return this;
    }

    /// <summary>Builds the <see cref="VectorCollectionDefinition"/>.</summary>
    /// <returns>
    /// A failed <see cref="Result{T}"/> with <c>IntelligenceErrors.InvalidCollectionDefinition</c> when
    /// <see cref="EmbeddingModel"/> or <see cref="DistanceMetric"/> was never called, the dimension is
    /// not positive, a duplicate/empty field name exists, or a <see cref="TenantField"/> names a field
    /// that is not declared <see cref="VectorFieldDefinition.Filterable"/>; otherwise a successful
    /// <see cref="Result{T}"/>.
    /// </returns>
    public Result<VectorCollectionDefinition> Build()
    {
        if (_embeddingModelId is null || _dimension is null)
        {
            return Result<VectorCollectionDefinition>.Failure(
                IntelligenceErrors.InvalidCollectionDefinition("EmbeddingModel(modelId, dimension) must be called before Build()."));
        }

        if (_metric is null)
        {
            return Result<VectorCollectionDefinition>.Failure(
                IntelligenceErrors.InvalidCollectionDefinition("DistanceMetric(metric) must be called before Build()."));
        }

        var structuralError = VectorCollectionDefinition.ValidateStructure(_name, _embeddingModelId, _dimension.Value);
        if (structuralError is not null)
        {
            return Result<VectorCollectionDefinition>.Failure(structuralError);
        }

        var fieldError = VectorCollectionDefinition.ValidateFields(_fields);
        if (fieldError is not null)
        {
            return Result<VectorCollectionDefinition>.Failure(fieldError);
        }

        if (_tenantField is not null)
        {
            var tenantField = _fields.Find(f => string.Equals(f.Name, _tenantField, StringComparison.Ordinal));
            if (tenantField is null || !tenantField.Filterable)
            {
                return Result<VectorCollectionDefinition>.Failure(
                    IntelligenceErrors.InvalidCollectionDefinition(
                        $"TenantField '{_tenantField}' must name a field declared Filterable."));
            }
        }

        return Result<VectorCollectionDefinition>.Success(new VectorCollectionDefinition
        {
            Name = _name,
            EmbeddingModelId = _embeddingModelId,
            Dimension = _dimension.Value,
            DistanceMetric = _metric.Value,
            TenantField = _tenantField,
            Fields = _fields.ToArray(),
        });
    }
}
