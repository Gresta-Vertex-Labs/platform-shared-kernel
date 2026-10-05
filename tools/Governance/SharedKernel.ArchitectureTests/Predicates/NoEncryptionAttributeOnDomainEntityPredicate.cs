using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (SK0302) that fails any type carrying a custom attribute whose
/// name contains <c>"Encrypt"</c> as a case-insensitive substring.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.EncryptionPatternGuardRules"/> to enforce that domain entity classes
/// must never be annotated with infrastructure-specific encryption attributes (e.g.,
/// <c>[Encrypted]</c>, <c>[EncryptedColumn]</c>, <c>[EncryptAttribute]</c>,
/// <c>[ShouldEncrypt]</c>). These attributes leak infrastructure concerns into the domain layer
/// and bypass <c>EncryptionModelConvention</c>, preventing the platform key-rotation lifecycle
/// from operating correctly.
/// </para>
/// <para>
/// Detection is via <see cref="TypeDefinition.CustomAttributes"/> enumeration — no IL instruction
/// walk is required. The case-insensitive substring check on <c>"Encrypt"</c> deliberately
/// catches all common attribute-naming conventions.
/// </para>
/// <para>
/// The predicate is always scoped by the caller to the domain assembly under test; non-domain
/// types are never passed to this rule.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>[EncryptedColumn] public string Ssn { get; private set; }  // on a domain entity class</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>builder.Property(x =&gt; x.Ssn).Encrypt();  // inside IEntityTypeConfiguration&lt;Order&gt;.Configure()</code>
/// </para>
/// </remarks>
public sealed class NoEncryptionAttributeOnDomainEntityPredicate : ICustomRule
{
    /// <summary>
    /// Returns <see langword="true"/> (rule met) when the type carries no custom attribute whose
    /// name contains <c>"Encrypt"</c> (case-insensitive); <see langword="false"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when any custom attribute whose name contains <c>"Encrypt"</c>
    /// is found; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        if (!type.HasCustomAttributes)
            return true;

        foreach (var attribute in type.CustomAttributes)
        {
            if (attribute.AttributeType.Name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
