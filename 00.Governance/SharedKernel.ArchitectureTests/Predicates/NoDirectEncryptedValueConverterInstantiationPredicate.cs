using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (SK0304) that fails any <c>IEntityTypeConfiguration&lt;T&gt;</c>
/// implementor — other than <c>EncryptionModelConvention</c> itself — whose method bodies
/// contain a <c>newobj</c> IL instruction targeting a type whose name contains
/// <c>"EncryptedValueConverter"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.EncryptionPatternGuardRules"/> to enforce that
/// <c>EncryptedValueConverter&lt;T&gt;</c> is never directly instantiated in EF Core
/// configuration classes. Direct instantiation bypasses <c>EncryptionModelConvention</c>,
/// causing either duplicate converter registration (double-encryption) or inconsistent
/// key-version handling across the model.
/// </para>
/// <para>
/// <strong>Type-scope guard (first check):</strong> Types whose
/// <see cref="TypeDefinition.Interfaces"/> does NOT contain any entry with
/// <see cref="InterfaceImplementation.InterfaceType"/>.<see cref="MemberReference.Name"/>
/// starting with <c>"IEntityTypeConfiguration"</c> are returned as passing
/// (<see langword="true"/>) unconditionally — the rule only targets EF Core configuration
/// classes.
/// </para>
/// <para>
/// <strong>Convention exemption (second check):</strong> Types whose
/// <see cref="TypeDefinition.Name"/> equals <c>"EncryptionModelConvention"</c> (exact match)
/// return <see langword="true"/> unconditionally — the convention is the sole legitimate
/// instantiation site and must never be flagged.
/// </para>
/// <para>
/// Detection: walks <see cref="TypeDefinition.Methods"/>.<see cref="MethodDefinition.Body"/>
/// .<see cref="Mono.Cecil.Cil.MethodBody.Instructions"/> for <c>newobj</c> opcodes where the
/// operand <see cref="MethodReference"/>.<see cref="MethodReference.DeclaringType"/>
/// .<see cref="MemberReference.Name"/> contains <c>"EncryptedValueConverter"</c> as a
/// substring.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>
/// builder.Property(x =&gt; x.Ssn)
///     .HasConversion(new EncryptedValueConverter&lt;string&gt;(options));
/// </code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>builder.Property(x =&gt; x.Ssn).Encrypt();</code>
/// </para>
/// </remarks>
public sealed class NoDirectEncryptedValueConverterInstantiationPredicate : ICustomRule
{
    private const string EntityTypeConfigurationPrefix = "IEntityTypeConfiguration";
    private const string ConventionExemptName = "EncryptionModelConvention";
    private const string ConverterNameSubstring = "EncryptedValueConverter";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for non-<c>IEntityTypeConfiguration</c> types,
    /// the <c>EncryptionModelConvention</c> itself, and any configuration type that does not
    /// directly instantiate <c>EncryptedValueConverter&lt;T&gt;</c>; <see langword="false"/>
    /// when a direct instantiation is found.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a <c>newobj EncryptedValueConverter&lt;T&gt;</c> instruction
    /// is found in an <c>IEntityTypeConfiguration&lt;T&gt;</c> implementor other than
    /// <c>EncryptionModelConvention</c>; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Type-scope guard — only IEntityTypeConfiguration<T> implementors are subject to this rule
        if (!ImplementsEntityTypeConfiguration(type))
            return true;

        // Convention exemption — the convention is the sole legitimate instantiation site
        if (type.Name == ConventionExemptName)
            return true;

        // IL walk — search for newobj targeting EncryptedValueConverter
        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Newobj)
                    continue;

                if (instruction.Operand is MethodReference methodRef &&
                    methodRef.DeclaringType.Name.Contains(ConverterNameSubstring, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ImplementsEntityTypeConfiguration(TypeDefinition type)
    {
        if (!type.HasInterfaces)
            return false;

        foreach (var iface in type.Interfaces)
        {
            if (iface.InterfaceType.Name.StartsWith(EntityTypeConfigurationPrefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
