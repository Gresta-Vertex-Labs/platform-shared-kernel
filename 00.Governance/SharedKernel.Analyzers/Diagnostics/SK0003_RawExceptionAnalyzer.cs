using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0003 — Fires when <c>throw new Exception(...)</c> or <c>throw new ApplicationException(...)</c>
/// is used without an <c>Error</c> payload argument.
/// Use <c>Result&lt;T&gt;.Failure(error)</c> or a typed SharedKernel exception carrying an Error instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawExceptionAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0003";

    /// <summary>The diagnostic descriptor for SK0003.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Raw Exception or ApplicationException throw",
        messageFormat: "Throwing '{0}' directly is not allowed — use Result<T>.Failure(error) or a typed SharedKernel exception carrying an Error payload",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0003-rawexceptionthrow"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeThrowStatement, SyntaxKind.ThrowStatement);
        context.RegisterSyntaxNodeAction(AnalyzeThrowExpression, SyntaxKind.ThrowExpression);
    }

    private static void AnalyzeThrowStatement(SyntaxNodeAnalysisContext context)
    {
        var throwStatement = (ThrowStatementSyntax)context.Node;
        if (throwStatement.Expression is ObjectCreationExpressionSyntax creation)
            AnalyzeCreation(context, creation);
    }

    private static void AnalyzeThrowExpression(SyntaxNodeAnalysisContext context)
    {
        var throwExpression = (ThrowExpressionSyntax)context.Node;
        if (throwExpression.Expression is ObjectCreationExpressionSyntax creation)
            AnalyzeCreation(context, creation);
    }

    private static void AnalyzeCreation(
        SyntaxNodeAnalysisContext context,
        ObjectCreationExpressionSyntax creation
    )
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(creation);
        var thrownType = typeInfo.Type;

        if (thrownType is null)
            return;

        if (!IsForbiddenExceptionType(thrownType))
            return;

        // Suppress if any argument resolves to Error type
        if (HasErrorArgument(creation, context.SemanticModel))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, creation.GetLocation(), thrownType.Name)
        );
    }

    private static bool IsForbiddenExceptionType(ITypeSymbol type)
    {
        // Only flag the root raw exception types — not their subclasses.
        // The rule targets: throw new Exception(...) and throw new ApplicationException(...).
        // Typed subclasses (DomainException, ArgumentException, etc.) are permitted.
        var fullName = type.ToDisplayString();
        return fullName == "System.Exception" || fullName == "System.ApplicationException";
    }

    private static bool HasErrorArgument(
        ObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel
    )
    {
        if (creation.ArgumentList is null)
            return false;

        foreach (var argument in creation.ArgumentList.Arguments)
        {
            var argTypeInfo = semanticModel.GetTypeInfo(argument.Expression);
            var argType = argTypeInfo.Type;
            if (argType is not null && argType.Name == "Error")
                return true;
        }

        return false;
    }
}
