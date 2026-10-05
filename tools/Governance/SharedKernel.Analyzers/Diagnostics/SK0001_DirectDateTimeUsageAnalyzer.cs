using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0001 — Fires on a <see cref="MemberAccessExpressionSyntax"/> of the shape
/// <c>SomeType.Member</c> or the fully qualified <c>System.SomeType.Member</c> whose type/member
/// text pair is <c>DateTime.UtcNow</c>, <c>DateTime.Now</c>, <c>DateTimeOffset.UtcNow</c>, or
/// <c>DateTimeOffset.Now</c>, outside the <c>SharedKernel.Primitives</c> namespace.
/// </summary>
/// <remarks>
/// <para>
/// Direct wall-clock access makes time-dependent logic impossible to control deterministically in
/// a test — there is nothing to substitute a fixed or advancing instant for the real clock. The
/// platform's sanctioned substitute is <c>IClock</c> (<c>SharedKernel.Primitives</c>), injected via
/// DI, so a caller can supply a controllable clock instead of reaching for the BCL directly.
/// <c>DateTimeOffset.Now</c> is the worst offender of the four: it additionally reads the machine's
/// local timezone, which is exactly the server-timezone-dependence class of bug <c>IClock</c> exists
/// to prevent.
/// </para>
/// <para>
/// <strong>Syntax-only, no <see cref="SemanticModel"/>.</strong> This is the domain's simplest
/// analyzer: the type/member pair is matched purely on identifier text, with no symbol resolution
/// at all. The receiver is accepted in two shapes: a bare <see cref="IdentifierNameSyntax"/>
/// (<c>DateTime.UtcNow</c>) or a fully qualified <see cref="MemberAccessExpressionSyntax"/> whose
/// own receiver is the bare identifier <c>System</c> (<c>System.DateTime.UtcNow</c>). The
/// <c>System</c> receiver is required specifically — matching on the type name alone would
/// false-positive on an unrelated property/field chain such as <c>SomeHolder.DateTime.UtcNow</c>,
/// where <c>DateTime</c> merely happens to be a member name on some other type. Any other qualified
/// receiver shape (an alias, a variable, a deeper chain) still does not match and does not fire —
/// a deliberate trade-off that keeps the check cheap for what is expected to be a very high-volume
/// syntax kind across every consuming project.
/// </para>
/// <para>
/// <strong>Deliberately narrow member set.</strong> Only the four members named above are
/// forbidden. <c>DateTime.Today</c> is never in scope at all, since it returns a date-only value
/// with different testability characteristics than the "current instant" accessors this rule
/// targets.
/// </para>
/// <para>
/// <strong>Suppression:</strong> any <see cref="NamespaceDeclarationSyntax"/> or
/// <see cref="FileScopedNamespaceDeclarationSyntax"/> ancestor whose name starts with
/// <c>SharedKernel.Primitives</c> (a prefix check, not an exact-segment match) suppresses the
/// diagnostic — <c>IClock</c>'s own implementation necessarily accesses the real clock somewhere,
/// and that is the one place direct wall-clock access is legitimate.
/// </para>
/// <para>
/// <strong>Fix:</strong> inject <c>IClock</c> via DI and call <c>clock.UtcNow</c> instead of the
/// BCL member directly.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectDateTimeUsageAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0001";

    /// <summary>The diagnostic descriptor for SK0001.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Direct DateTime/DateTimeOffset usage",
        messageFormat: "Direct access to '{0}' is not allowed — inject IClock via DI instead",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0001-directdatetimeusage"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeMemberAccess,
            SyntaxKind.SimpleMemberAccessExpression
        );
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // Must be: SomeType.MemberName, or the fully qualified System.SomeType.MemberName
        if (!TryGetReceiverTypeName(memberAccess.Expression, out var typeName))
            return;

        var memberName = memberAccess.Name.Identifier.ValueText;

        bool isViolation =
            (typeName == "DateTime" && (memberName == "UtcNow" || memberName == "Now"))
            || (typeName == "DateTimeOffset" && (memberName == "UtcNow" || memberName == "Now"));

        if (!isViolation)
            return;

        // Suppress inside SharedKernel.Primitives namespace
        if (IsInsidePrimitivesNamespace(memberAccess))
            return;

        var fullAccess = $"{typeName}.{memberName}";
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, memberAccess.GetLocation(), fullAccess)
        );
    }

    /// <summary>
    /// Resolves the receiver's type-name text for either a bare <c>SomeType.Member</c> access or a
    /// fully qualified <c>System.SomeType.Member</c> access. The <c>System</c> receiver is checked
    /// by identifier text, not symbol resolution — this stays syntax-only — and is required
    /// verbatim so an unrelated <c>SomeHolder.DateTime.UtcNow</c>-shaped property/field chain (where
    /// <c>DateTime</c> is merely a member name on some other type) is never mistaken for the BCL
    /// type.
    /// </summary>
    private static bool TryGetReceiverTypeName(ExpressionSyntax receiver, out string typeName)
    {
        // Bare: SomeType.MemberName
        if (receiver is IdentifierNameSyntax identifier)
        {
            typeName = identifier.Identifier.ValueText;
            return true;
        }

        // Fully qualified: System.SomeType.MemberName
        if (
            receiver is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "System" },
                Name: IdentifierNameSyntax typeIdentifier
            }
        )
        {
            typeName = typeIdentifier.Identifier.ValueText;
            return true;
        }

        typeName = string.Empty;
        return false;
    }

    private static bool IsInsidePrimitivesNamespace(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (
                current is NamespaceDeclarationSyntax namespaceDecl
                && namespaceDecl.Name.ToString().StartsWith("SharedKernel.Primitives")
            )
                return true;

            if (
                current is FileScopedNamespaceDeclarationSyntax fileScopedNs
                && fileScopedNs.Name.ToString().StartsWith("SharedKernel.Primitives")
            )
                return true;

            current = current.Parent;
        }

        return false;
    }
}
