namespace SharedKernel.Persistence.PostgreSQL.Jsonb;

/// <summary>
/// Marks a property for JSONB column storage in PostgreSQL.
/// </summary>
/// <remarks>
/// Apply this attribute to document the intent; use <c>HasJsonbColumn&lt;TProperty&gt;</c> on
/// <c>EntityTypeBuilder&lt;T&gt;</c> to configure the column type in EF Core.
/// STJ serialization is configured globally by Npgsql — no per-column converter is needed.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class JsonbColumnAttribute : Attribute
{
}
