using System.Text.RegularExpressions;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Generates the <c>BEFORE UPDATE/DELETE/TRUNCATE</c> rejection triggers shared by the ledger schema and
/// <c>CreateImmutabilityTrigger</c>. Every trigger is switched to <c>ENABLE ALWAYS</c>, so setting
/// <c>session_replication_role = replica</c> does not bypass it.
/// </summary>
internal static partial class ImmutabilityTriggerSql
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SimpleIdentifierPattern { get; }

    public static string FunctionName(string? schema, string table) => Qualify(schema, $"{table}_reject_mutation");

    public static IReadOnlyList<string> TriggerNames(string table, bool allowDelete) =>
        allowDelete
            ? [$"{table}_reject_update", $"{table}_reject_truncate"]
            : [$"{table}_reject_update", $"{table}_reject_delete", $"{table}_reject_truncate"];

    public static IReadOnlyList<string> Statements(string? schema, string table, bool allowDelete)
    {
        var qualifiedTable = Qualify(schema, table);
        var function = FunctionName(schema, table);
        var update = Quote($"{table}_reject_update");
        var delete = Quote($"{table}_reject_delete");
        var truncate = Quote($"{table}_reject_truncate");
        var rejected = allowDelete ? "updated or truncated" : "updated, deleted, or truncated";

        List<string> statements =
        [
            $"""
            CREATE OR REPLACE FUNCTION {function}()
            RETURNS trigger AS $reject_mutation$
            BEGIN
                RAISE EXCEPTION '% is an append-only table; rows cannot be {rejected}', TG_TABLE_NAME
                    USING ERRCODE = 'insufficient_privilege';
            END;
            $reject_mutation$ LANGUAGE plpgsql;
            """,
            $"DROP TRIGGER IF EXISTS {update} ON {qualifiedTable};",
            $"CREATE TRIGGER {update} BEFORE UPDATE ON {qualifiedTable} FOR EACH ROW EXECUTE FUNCTION {function}();",
            $"ALTER TABLE {qualifiedTable} ENABLE ALWAYS TRIGGER {update};",
        ];

        if (!allowDelete)
        {
            statements.Add($"DROP TRIGGER IF EXISTS {delete} ON {qualifiedTable};");
            statements.Add($"CREATE TRIGGER {delete} BEFORE DELETE ON {qualifiedTable} FOR EACH ROW EXECUTE FUNCTION {function}();");
            statements.Add($"ALTER TABLE {qualifiedTable} ENABLE ALWAYS TRIGGER {delete};");
        }

        statements.Add($"DROP TRIGGER IF EXISTS {truncate} ON {qualifiedTable};");
        statements.Add($"CREATE TRIGGER {truncate} BEFORE TRUNCATE ON {qualifiedTable} FOR EACH STATEMENT EXECUTE FUNCTION {function}();");
        statements.Add($"ALTER TABLE {qualifiedTable} ENABLE ALWAYS TRIGGER {truncate};");
        return statements;
    }

    public static IReadOnlyList<string> DropStatements(string? schema, string table)
    {
        var qualifiedTable = Qualify(schema, table);
        return
        [
            $"DROP TRIGGER IF EXISTS {Quote($"{table}_reject_update")} ON {qualifiedTable};",
            $"DROP TRIGGER IF EXISTS {Quote($"{table}_reject_delete")} ON {qualifiedTable};",
            $"DROP TRIGGER IF EXISTS {Quote($"{table}_reject_truncate")} ON {qualifiedTable};",
            $"DROP FUNCTION IF EXISTS {FunctionName(schema, table)}();",
        ];
    }

    /// <summary>Validates <paramref name="identifier"/> is a simple identifier and double-quotes it.</summary>
    /// <exception cref="ArgumentException">The identifier is not a simple PostgreSQL identifier.</exception>
    public static string Quote(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        if (!SimpleIdentifierPattern.IsMatch(identifier))
        {
            throw new ArgumentException(
                $"'{identifier}' is not a simple PostgreSQL identifier (letters, digits, underscore, not starting with a digit).",
                nameof(identifier));
        }

        return $"\"{identifier}\"";
    }

    public static string Qualify(string? schema, string name) =>
        schema is null ? Quote(name) : $"{Quote(schema)}.{Quote(name)}";
}
