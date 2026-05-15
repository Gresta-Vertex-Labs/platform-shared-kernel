using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0004 — Fires when a <c>return null</c> literal appears inside a method whose declared
/// return type is <c>Error</c> or <c>Error?</c> (nullable Error).
/// Return <c>Error.None</c> to signal "no error" — never return <c>null</c> for Error.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullErrorReturnAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0004";

    /// <summary>The diagnostic descriptor for SK0004.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Null return for Error type",
        messageFormat: "Returning null for type 'Error' is not allowed — return 'Error.None' to indicate the absence of an error",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0004-nullerrorreturn"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeReturnStatement, SyntaxKind.ReturnStatement);
    }

    private static void AnalyzeReturnStatement(SyntaxNodeAnalysisContext context)
    {
        var returnStatement = (ReturnStatementSyntax)context.Node;

        // Must be: return null;
        if (returnStatement.Expression is not LiteralExpressionSyntax literal)
            return;
        if (!literal.IsKind(SyntaxKind.NullLiteralExpression))
            return;

        // Find the enclosing method/local function to inspect its return type
        var containingMethod = GetContainingMethod(returnStatement);
        if (containingMethod is null)
            return;

        var returnTypeSymbol = GetReturnTypeSymbol(containingMethod, context.SemanticModel);
        if (!IsErrorType(returnTypeSymbol))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, returnStatement.GetLocation()));
    }

    private static SyntaxNode? GetContainingMethod(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (
                current is MethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AccessorDeclarationSyntax
            )
                return current;

            // Stop at class/struct boundaries to avoid walking too far
            if (
                current is ClassDeclarationSyntax
                or StructDeclarationSyntax
                or RecordDeclarationSyntax
            )
                return null;

            current = current.Parent;
        }

        return null;
    }

    private static ITypeSymbol? GetReturnTypeSymbol(SyntaxNode method, SemanticModel semanticModel)
    {
        if (method is MethodDeclarationSyntax methodDecl)
        {
            var symbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
            return symbol?.ReturnType;
        }

        if (method is LocalFunctionStatementSyntax localFunc)
        {
            var symbol = semanticModel.GetDeclaredSymbol(localFunc) as IMethodSymbol;
            return symbol?.ReturnType;
        }

        if (method is AccessorDeclarationSyntax accessor)
        {
            // Property accessor — get the property's type
            if (accessor.Parent?.Parent is PropertyDeclarationSyntax propertyDecl)
            {
                var typeInfo = semanticModel.GetTypeInfo(propertyDecl.Type);
                return typeInfo.Type;
            }
        }

        return null;
    }

    private static bool IsErrorType(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        // Handle nullable Error? — unwrap the nullable wrapper
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            // Nullable<Error> is represented as a named type with nullability annotation
            if (
                namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
                && namedType.TypeArguments.Length == 1
            )
                return namedType.TypeArguments[0].Name == "Error";
        }

        // Check for nullable reference annotation (Error?)
        if (type.NullableAnnotation == NullableAnnotation.Annotated && type.Name == "Error")
            return true;

        return type.Name == "Error";
    }
}
