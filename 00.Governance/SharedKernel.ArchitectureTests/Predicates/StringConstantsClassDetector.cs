using Mono.Cecil;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Reusable Mono.Cecil helper that detects the "all-string-constants static class" IL shape
/// and resolves the literal values exposed by every field on a qualifying type.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not an <c>ICustomRule</c>.</strong> This is a structural/value-resolution helper,
/// not a NetArchTest predicate in its own right. <see cref="NoBareHealthCheckLiteralWhereConstantsExistPredicate"/>
/// and any future similarly-shaped rule call this helper directly to build the set of known
/// constant string values in an assembly.
/// </para>
/// <para>
/// <strong>Shape contract.</strong> A type qualifies as a "string constants class" when:
/// <see cref="TypeDefinition.IsAbstract"/> and <see cref="TypeDefinition.IsSealed"/> are both
/// <see langword="true"/> (the C# <c>static class</c> IL shape), the type declares at least one
/// field, and every field on the type is either:
/// <list type="bullet">
/// <item><description>
/// <see cref="FieldDefinition.IsLiteral"/> with <c>FieldType.FullName == "System.String"</c>
/// (a <c>const string</c>), or
/// </description></item>
/// <item><description>
/// <see cref="FieldDefinition.IsInitOnly"/> and <see cref="FieldDefinition.IsStatic"/> with
/// <c>FieldType.FullName == "System.String"</c> (a <c>static readonly string</c>).
/// </description></item>
/// </list>
/// Mixed-type constants classes (containing non-string constants alongside string constants)
/// still qualify — only the string-typed fields contribute literal values to the resolved set;
/// non-string fields are ignored, not disqualifying.
/// </para>
/// <para>
/// <strong>Value resolution.</strong> For <c>const string</c> fields, the literal value is read
/// directly from <see cref="FieldDefinition.Constant"/>. <c>static readonly string</c> fields do
/// not carry a compile-time constant value in IL metadata (they are assigned in the type's static
/// constructor) — this helper resolves their value by walking the static constructor's IL body
/// for an <c>Ldstr</c> instruction immediately followed by an <c>Stsfld</c> referencing the field.
/// </para>
/// </remarks>
public static class StringConstantsClassDetector
{
    /// <summary>
    /// A single resolved (declaring type name, field name, literal value) tuple produced by
    /// <see cref="ResolveStringConstants"/>.
    /// </summary>
    /// <param name="DeclaringTypeName">
    /// The simple name of the type declaring the field (e.g. a well-known-string constants
    /// class such as <c>WidgetRegistrationNames</c>).
    /// </param>
    /// <param name="FieldName">The field's name (e.g. <c>"Primary"</c>).</param>
    /// <param name="Value">The field's resolved literal string value.</param>
    public readonly record struct ResolvedStringConstant(
        string DeclaringTypeName,
        string FieldName,
        string Value);

    /// <summary>
    /// Scans every type in <paramref name="module"/> for the "string constants class" shape and
    /// returns the resolved set of (declaring type name, field name, literal value) tuples across
    /// all qualifying types in the module.
    /// </summary>
    /// <param name="module">The Mono.Cecil module to scan (typically <c>type.Module</c>).</param>
    /// <returns>
    /// The resolved set of string constant tuples. Empty when no type in the module qualifies as
    /// a string constants class.
    /// </returns>
    public static IReadOnlyList<ResolvedStringConstant> ResolveStringConstants(ModuleDefinition module)
    {
        var resolved = new List<ResolvedStringConstant>();

        foreach (var type in module.Types)
        {
            if (!IsStringConstantsClass(type))
                continue;

            foreach (var constant in ResolveFieldValues(type))
                resolved.Add(constant);
        }

        return resolved;
    }

    /// <summary>
    /// Resolves every <c>const string</c>/<c>static readonly string</c> field declared DIRECTLY on
    /// <paramref name="type"/>, regardless of whether the type as a whole matches the "string
    /// constants class" shape (see <see cref="IsStringConstantsClass"/>) — i.e. a type may carry a
    /// mix of string and non-string fields, or may not be <c>abstract sealed</c> at all, and this
    /// method still resolves whatever qualifying string fields it declares.
    /// </summary>
    /// <remarks>
    /// Used by <c>WellKnownConstantOwnershipAssertion</c> (WO-042 P-264) to walk EVERY
    /// <c>TypeDefinition</c> in a scanned assembly — not only types matching the constants-class
    /// shape — so a stray <c>const</c>/<c>static readonly string</c> field on an ordinary class is
    /// caught too.
    /// </remarks>
    /// <param name="type">The Mono.Cecil type to inspect.</param>
    /// <returns>
    /// The resolved set of (declaring type name, field name, literal value) tuples for
    /// <paramref name="type"/>'s own qualifying fields. Empty when the type declares no
    /// <c>const</c>/<c>static readonly string</c> field.
    /// </returns>
    public static IReadOnlyList<ResolvedStringConstant> ResolveStringFieldsOnType(TypeDefinition type) =>
        ResolveFieldValues(type).ToList();

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> matches the "string constants
    /// class" shape: <c>static class</c> (abstract + sealed) with at least one field, where every
    /// field is a <c>const string</c> or <c>static readonly string</c>.
    /// </summary>
    public static bool IsStringConstantsClass(TypeDefinition type)
    {
        if (!type.IsAbstract || !type.IsSealed)
            return false;

        if (type.Fields.Count == 0)
            return false;

        foreach (var field in type.Fields)
        {
            if (IsConstString(field) || IsStaticReadonlyString(field))
                continue;

            return false;
        }

        return true;
    }

    private static bool IsConstString(FieldDefinition field) =>
        field.IsLiteral && field.FieldType.FullName == "System.String";

    private static bool IsStaticReadonlyString(FieldDefinition field) =>
        field.IsInitOnly && field.IsStatic && field.FieldType.FullName == "System.String";

    private static IEnumerable<ResolvedStringConstant> ResolveFieldValues(TypeDefinition type)
    {
        var staticConstructorLiterals = ResolveStaticConstructorFieldLiterals(type);

        foreach (var field in type.Fields)
        {
            if (IsConstString(field) && field.Constant is string constLiteral)
            {
                yield return new ResolvedStringConstant(type.Name, field.Name, constLiteral);
                continue;
            }

            if (IsStaticReadonlyString(field)
                && staticConstructorLiterals.TryGetValue(field.Name, out var readonlyLiteral))
            {
                yield return new ResolvedStringConstant(type.Name, field.Name, readonlyLiteral);
            }
        }
    }

    /// <summary>
    /// Walks the type's static constructor (<c>.cctor</c>) IL body collecting
    /// <c>Ldstr</c> → <c>Stsfld</c> pairs, producing a map of field name to assigned literal
    /// value. Used to resolve <c>static readonly string</c> field values, which are not exposed
    /// as compile-time constants in IL metadata.
    /// </summary>
    private static Dictionary<string, string> ResolveStaticConstructorFieldLiterals(TypeDefinition type)
    {
        var literals = new Dictionary<string, string>(StringComparer.Ordinal);

        var staticConstructor = type.Methods.FirstOrDefault(m => m.Name == ".cctor");
        if (staticConstructor?.Body is null)
            return literals;

        var instructions = staticConstructor.Body.Instructions;

        for (var i = 0; i < instructions.Count - 1; i++)
        {
            if (instructions[i].OpCode != Mono.Cecil.Cil.OpCodes.Ldstr
                || instructions[i].Operand is not string literal)
                continue;

            var next = instructions[i + 1];
            if (next.OpCode == Mono.Cecil.Cil.OpCodes.Stsfld
                && next.Operand is FieldReference fieldReference
                && fieldReference.DeclaringType.FullName == type.FullName)
            {
                literals[fieldReference.Name] = literal;
            }
        }

        return literals;
    }
}
