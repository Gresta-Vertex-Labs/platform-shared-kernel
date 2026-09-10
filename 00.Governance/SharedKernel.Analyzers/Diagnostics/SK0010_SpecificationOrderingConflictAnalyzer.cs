using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0010 — Fires when a <see cref="ConstructorDeclarationSyntax"/>'s body (block or
/// expression-bodied) contains, anywhere in its full descendant tree, both an invocation whose
/// simple method name is <c>ApplyOrderBy</c> and one whose simple method name is
/// <c>ApplyOrderByDescending</c>.
/// </summary>
/// <remarks>
/// <para>
/// Calling both ordering methods in the same constructor produces non-deterministic sort results
/// at query execution time because the last call overwrites the previous ordering direction
/// (both set the same <c>OrderBy</c>/<c>OrderByDescending</c> property). Use only one ordering
/// direction per specification constructor; apply secondary sorting via <c>ThenBy</c> /
/// <c>ThenByDescending</c> overloads if needed.
/// </para>
/// <para>
/// Simple name check on invocation method names. No type-scoping to <c>Specification&lt;T&gt;</c>
/// subclasses required — the method names are unique within the SDK.
/// </para>
/// <para>
/// <strong>Whole-subtree scan, not statement-level.</strong> The two method-name flags are set by
/// scanning EVERY <see cref="InvocationExpressionSyntax"/> under
/// <see cref="ConstructorDeclarationSyntax.Body"/> (or
/// <see cref="ConstructorDeclarationSyntax.ExpressionBody"/>) via <c>DescendantNodes()</c> — not
/// just top-level statements. A call nested inside a lambda, local function, or conditional branch
/// declared INSIDE the constructor still counts, with no receiver-identity tracking to confirm both
/// calls target the same specification instance (unlike SK0032's symbol-tracked CORS check). This
/// is a deliberate over-approximation: since the two method names really are unique to this SDK's
/// specification-building surface, the risk of a false positive from an unrelated same-named method
/// is treated as negligible.
/// </para>
/// <para>
/// <strong>Early-exit, not exhaustive.</strong> The scan short-circuits the moment both flags are
/// simultaneously <see langword="true"/> — the order of appearance and any calls beyond the first
/// pair are never inspected, since a single conflicting pair is already sufficient to report.
/// </para>
/// <para>
/// <strong>Pass cases:</strong> a constructor calling only one of the two methods, or neither, never
/// fires — including a constructor that never touches ordering at all.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SpecificationOrderingConflictAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0010";

    private const string ApplyOrderByName = "ApplyOrderBy";
    private const string ApplyOrderByDescendingName = "ApplyOrderByDescending";

    /// <summary>The diagnostic descriptor for SK0010.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Specification constructor has conflicting ordering calls",
        messageFormat: "Constructor '{0}' calls both ApplyOrderBy and ApplyOrderByDescending — use only one primary ordering direction and apply secondary sorting via ThenBy/ThenByDescending",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0010-specificationorderingconflict"
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
            AnalyzeConstructor,
            SyntaxKind.ConstructorDeclaration
        );
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var ctor = (ConstructorDeclarationSyntax)context.Node;

        if (ctor.Body is null && ctor.ExpressionBody is null)
            return;

        bool hasApplyOrderBy = false;
        bool hasApplyOrderByDescending = false;

        // Collect all invocation expressions in the constructor body
        IEnumerable<InvocationExpressionSyntax> invocations;

        if (ctor.Body is not null)
        {
            invocations = ctor.Body
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>();
        }
        else
        {
            invocations = ctor.ExpressionBody!
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>();
        }

        foreach (var invocation in invocations)
        {
            var methodName = GetInvocationMethodName(invocation);
            if (methodName == ApplyOrderByName)
                hasApplyOrderBy = true;
            else if (methodName == ApplyOrderByDescendingName)
                hasApplyOrderByDescending = true;

            if (hasApplyOrderBy && hasApplyOrderByDescending)
                break;
        }

        if (hasApplyOrderBy && hasApplyOrderByDescending)
        {
            var className = ctor.Identifier.Text;
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, ctor.Identifier.GetLocation(), className)
            );
        }
    }

    private static string GetInvocationMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            _ => string.Empty,
        };
    }
}
