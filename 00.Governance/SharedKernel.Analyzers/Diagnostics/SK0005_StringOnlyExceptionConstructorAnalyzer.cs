using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0005 — Fires on an <see cref="ObjectCreationExpressionSyntax"/> with exactly one argument
/// that is a string-literal <see cref="LiteralExpressionSyntax"/>, when the constructed type's
/// <see cref="ITypeSymbol.BaseType"/> chain contains a type simply named
/// <c>SharedKernelException</c> (e.g., <c>new DomainException("message")</c>).
/// </summary>
/// <remarks>
/// <para>
/// A <c>SharedKernelException</c> subclass constructed with only a free-text string discards the
/// same machine-readable <c>Error</c> payload SK0003 already requires for the raw BCL exception
/// types — the platform's typed exception hierarchy exists specifically so an <c>Error</c> can
/// travel with the throw, and a string-only constructor call defeats that even though the TYPE
/// itself is the sanctioned one.
/// </para>
/// <para>
/// <strong>Base-type walk, not base-list syntax.</strong> Unlike SK0009's syntactic
/// <c>BaseList</c> check, this rule walks the resolved <see cref="INamedTypeSymbol.BaseType"/>
/// chain via the semantic model, following inheritance until it either finds a type named
/// <c>SharedKernelException</c> or runs out of base types. This correctly reaches an
/// indirect/multi-level subclass — e.g. <c>DomainException : SharedKernelException</c> and anything
/// further deriving from <c>DomainException</c> — without needing a separate check per inheritance
/// depth.
/// </para>
/// <para>
/// <strong>Argument-shape gate, checked before the base-type walk.</strong> Only an EXACT
/// single-argument, string-literal call shape is inspected at all — a two-argument constructor
/// (<c>new DomainException("CODE", "message")</c>) or a single non-string-literal argument (e.g. an
/// <c>Error</c> struct instance, rather than a string literal) never even reaches the base-type
/// check and passes automatically. This is deliberately cheap and conservative: it flags only the
/// unambiguous "a literal string was passed as the sole argument" shape, not every possible way an
/// <c>Error</c> payload could be lost.
/// </para>
/// <para>
/// <strong>Pass case:</strong> an ordinary BCL exception (<c>ArgumentException</c>, etc.) that does
/// not derive from anything named <c>SharedKernelException</c> is out of scope regardless of its
/// constructor arguments — this rule only ever governs the platform's own typed exception
/// hierarchy.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringOnlyExceptionConstructorAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0005";
    private const string SharedKernelExceptionName = "SharedKernelException";

    /// <summary>The diagnostic descriptor for SK0005.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "SharedKernelException subclass constructed with string only",
        messageFormat: "'{0}' is constructed with a string-only argument — supply an Error payload instead (e.g., new {0}(error))",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0005-stringonlyexceptionconstructor"
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
            AnalyzeObjectCreation,
            SyntaxKind.ObjectCreationExpression
        );
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        if (creation.ArgumentList is null)
            return;

        var arguments = creation.ArgumentList.Arguments;

        // Must have exactly one argument
        if (arguments.Count != 1)
            return;

        // That single argument must be a string literal
        if (!IsStringLiteral(arguments[0].Expression))
            return;

        // Resolve the type being constructed
        var typeInfo = context.SemanticModel.GetTypeInfo(creation);
        var constructedType = typeInfo.Type;
        if (constructedType is null)
            return;

        // Walk the base-type chain looking for SharedKernelException
        if (!DerivesFromSharedKernelException(constructedType))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, creation.GetLocation(), constructedType.Name)
        );
    }

    private static bool IsStringLiteral(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax literal
        && literal.IsKind(SyntaxKind.StringLiteralExpression);

    private static bool DerivesFromSharedKernelException(ITypeSymbol type)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.Name == SharedKernelExceptionName)
                return true;
            current = current.BaseType;
        }

        return false;
    }
}
