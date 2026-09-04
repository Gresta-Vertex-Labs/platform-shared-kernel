namespace SharedKernel.DataPrivacy.Classification;

/// <summary>
/// Marks a property or field with its <see cref="DataClassification"/> sensitivity tier.
/// </summary>
/// <remarks>
/// <para>
/// THIS ATTRIBUTE IS PURE METADATA. IT IS NEVER READ VIA REFLECTION IN PRODUCTION CODE. Its sole
/// sanctioned consumers are a compile-time Roslyn analyzer (<c>00.Governance</c>, out of this
/// package's jurisdiction) and human documentation/code review. The platform already bans
/// reflection-based property walks for structured logging (see the root and <c>01.Core</c>
/// <c>CLAUDE.md</c> files) — a classification mechanism that itself required runtime reflection to
/// be useful would directly contradict the rule it exists to support.
/// </para>
/// <para>
/// Usable on any type in any layer, including <c>03.Domain</c> and <c>04.Contracts</c>. Both of
/// those layers stay logging-free and dependency-minimal by platform rule; a pure, unread-at-runtime
/// metadata attribute violates neither constraint.
/// </para>
/// </remarks>
/// <param name="classification">The sensitivity tier the annotated member is classified under.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class DataClassificationAttribute(DataClassification classification) : Attribute
{
    /// <summary>The sensitivity tier the annotated member is classified under.</summary>
    public DataClassification Classification { get; } = classification;
}
