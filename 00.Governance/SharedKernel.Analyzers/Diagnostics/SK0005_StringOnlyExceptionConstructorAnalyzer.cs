using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0005 — Fires when a type derived from <c>SharedKernelException</c> is constructed
/// with a single string literal argument (e.g., <c>new DomainException("message")</c>).
/// Supply an <c>Error</c> payload instead: <c>new DomainException(error)</c>.
/// </summary>
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
