namespace SharedKernel.Persistence.PostgreSQL.Vector;

/// <summary>
/// Marks a <c>float[]</c> or <c>Pgvector.Vector</c> property for pgvector column storage in PostgreSQL.
/// </summary>
/// <remarks>
/// Apply this attribute to document intent; use <c>HasVectorColumn&lt;TProperty&gt;</c> on
/// <c>EntityTypeBuilder&lt;T&gt;</c> to configure the column type in EF Core.
/// Requires the <c>pgvector</c> extension to be enabled in the PostgreSQL instance.
/// Call <c>EnsureCreated()</c> or include <c>CREATE EXTENSION IF NOT EXISTS vector</c> in migrations.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class VectorColumnAttribute : Attribute
{
    /// <summary>
    /// Gets the number of dimensions for this vector column.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Initialises a new <see cref="VectorColumnAttribute"/> with the specified <paramref name="dimensions"/>.
    /// </summary>
    /// <param name="dimensions">The number of dimensions (must be greater than zero).</param>
    public VectorColumnAttribute(int dimensions)
    {
        if (dimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Vector dimensions must be greater than zero.");
        Dimensions = dimensions;
    }
}
