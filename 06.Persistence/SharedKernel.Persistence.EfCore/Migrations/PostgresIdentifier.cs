using System.Text.RegularExpressions;

namespace SharedKernel.Persistence.EfCore.Migrations;

/// <summary>
/// Validates and quotes a caller-supplied PostgreSQL identifier (table/column/schema/policy name)
/// used inside a migration-authoring helper's generated DDL.
/// </summary>
/// <remarks>
/// DDL identifiers cannot be passed as query parameters — this is not the SQL-injection surface
/// <c>SharedKernel.Persistence.Dapper</c>'s data-access methods guard against (caller-supplied query
/// values), it is migration-AUTHORING code, whose identifier arguments come from the developer
/// writing the migration, not runtime/user input. Validation here exists to fail loudly on a typo
/// (an unquoted space, a stray quote) rather than generate silently-broken DDL.
/// </remarks>
internal static partial class PostgresIdentifier
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SimpleIdentifierPattern { get; }

    /// <summary>
    /// Validates <paramref name="identifier"/> is a simple (unquoted-safe) PostgreSQL identifier and
    /// returns it wrapped in double quotes.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="identifier"/> is not a legal simple identifier.
    /// </exception>
    public static string Quote(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (!SimpleIdentifierPattern.IsMatch(identifier))
        {
            throw new ArgumentException(
                $"'{identifier}' is not a simple PostgreSQL identifier (letters, digits, underscore, "
                    + "not starting with a digit). Quote/escape it yourself before calling this "
                    + "helper if a non-simple identifier is genuinely required.",
                nameof(identifier));
        }

        return $"\"{identifier}\"";
    }

    /// <summary>Quotes <paramref name="table"/>, optionally schema-qualified by <paramref name="schema"/>.</summary>
    public static string QualifyTable(string? schema, string table) =>
        schema is null ? Quote(table) : $"{Quote(schema)}.{Quote(table)}";
}
