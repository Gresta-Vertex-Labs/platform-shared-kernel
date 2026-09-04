namespace SharedKernel.DataPrivacy.Classification;

/// <summary>
/// Marks a property or field with its <see cref="SensitiveDataCategory"/>.
/// </summary>
/// <remarks>
/// <para>
/// THIS ATTRIBUTE IS PURE METADATA. IT IS NEVER READ VIA REFLECTION IN PRODUCTION CODE. Its sole
/// sanctioned consumers are a compile-time Roslyn analyzer (<c>00.Governance</c>, out of this
/// package's jurisdiction) and human documentation/code review. See
/// <see cref="DataClassificationAttribute"/>'s remarks for the full rationale — it applies
/// identically here.
/// </para>
/// <para>
/// Usable on any type in any layer, including <c>03.Domain</c> and <c>04.Contracts</c>, for the
/// same reason documented on <see cref="DataClassificationAttribute"/>. A member may carry both
/// attributes at once — a category (<em>what kind</em> of sensitive data) and a classification
/// (<em>how sensitive</em> it is) are independent, orthogonal facts about the same member.
/// </para>
/// </remarks>
/// <param name="category">The sensitive-data category the annotated member belongs to.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SensitiveDataCategoryAttribute(SensitiveDataCategory category) : Attribute
{
    /// <summary>The sensitive-data category the annotated member belongs to.</summary>
    public SensitiveDataCategory Category { get; } = category;
}
