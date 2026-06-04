using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace SharedKernel.Persistence.PostgreSQL.Conventions;

/// <summary>
/// An EF Core <see cref="IModelFinalizingConvention"/> that converts all table names, column names,
/// index names, and constraint names to <c>snake_case</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered automatically when <c>UsePostgreSQL()</c> is called. The convention is added via
/// the DbContext's <c>ConfigureConventions(ModelConfigurationBuilder)</c> override.
/// </para>
/// <para>
/// Conversion is idempotent — names that are already lowercase and underscore-separated pass
/// through unchanged. <c>PascalCase</c>, <c>camelCase</c>, and
/// <c>UPPER_SNAKE_CASE</c> are all normalised to <c>lower_snake_case</c>.
/// </para>
/// </remarks>
public sealed class SnakeCaseNamingConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            // Table name
            entityType.SetTableName(ToSnakeCase(entityType.GetTableName() ?? entityType.ShortName()));

            // Column names
            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName()
                    ?? property.Name;
                property.SetColumnName(ToSnakeCase(columnName));
            }

            // Index names
            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName();
                if (!string.IsNullOrEmpty(indexName))
                    index.SetDatabaseName(ToSnakeCase(indexName));
            }

            // Foreign-key constraint names
            foreach (var fk in entityType.GetForeignKeys())
            {
                var constraintName = fk.GetConstraintName();
                if (!string.IsNullOrEmpty(constraintName))
                    fk.SetConstraintName(ToSnakeCase(constraintName));
            }
        }
    }

    /// <summary>
    /// Converts a <c>PascalCase</c>, <c>camelCase</c>, or already-snake-case identifier to
    /// <c>lower_snake_case</c>. Idempotent on already-snake-case input.
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Insert underscore between a lowercase letter (or digit) followed by an uppercase letter.
        var result = Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1_$2");
        // Insert underscore between consecutive uppercase letters followed by a lowercase letter.
        result = Regex.Replace(result, @"([A-Z]+)([A-Z][a-z])", "$1_$2");

        return result.ToLowerInvariant();
    }
}
