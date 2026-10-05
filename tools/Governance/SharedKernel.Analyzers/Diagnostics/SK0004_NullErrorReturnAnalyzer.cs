using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0004 — Fires on a bare <c>return null;</c> <see cref="ReturnStatementSyntax"/> inside a
/// method, local function, or property accessor whose resolved return type is <c>Error</c> or
/// nullable <c>Error</c> (<c>Error?</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>Error</c> already has a dedicated "no error" sentinel — <c>Error.None</c> — precisely so
/// callers never need <see langword="null"/> to express absence. A <c>return null;</c> forces every
/// caller of an <c>Error?</c>-returning member to null-check before it can safely inspect the
/// result, reintroducing the exact null-handling burden the platform's <c>Result</c>/<c>Error</c>
/// primitives exist to remove.
/// </para>
/// <para>
/// <strong>Containing-member resolution.</strong> <c>GetContainingMethod</c> walks
/// <see cref="SyntaxNode.Parent"/> looking for the nearest <see cref="MethodDeclarationSyntax"/>,
/// <see cref="LocalFunctionStatementSyntax"/>, or <see cref="AccessorDeclarationSyntax"/>, and
/// stops early — reporting no match — if it instead reaches a class/struct/record boundary first.
/// A property accessor is resolved differently from the other two shapes: its "return type" is read
/// from <c>semanticModel.GetTypeInfo(propertyDeclaration.Type)</c> on the enclosing
/// <see cref="PropertyDeclarationSyntax"/>, since an accessor declaration has no return-type syntax
/// of its own.
/// </para>
/// <para>
/// <strong>Three-shape <c>Error</c> match.</strong> <c>IsErrorType</c> recognizes: the resolved
/// type being literally named <c>Error</c>; the value-type nullable wrapper
/// <c>Nullable&lt;Error&gt;</c>, unwrapped via <c>ConstructedFrom.SpecialType ==
/// SpecialType.System_Nullable_T</c>; and a reference-type nullable annotation
/// (<c>NullableAnnotation.Annotated</c>) on a type named <c>Error</c>. Covering all three shapes
/// means the rule fires correctly whether the real <c>SharedKernel.Primitives.Error</c> is declared
/// as a value type or a reference type, without the analyzer needing to know which.
/// </para>
/// <para>
/// <strong>Simple-name match only.</strong> Like SK0005 and SK0009, the type check is
/// <c>type.Name == "Error"</c> with no namespace qualification required — a test fixture (or an
/// unrelated consuming type) can declare its own local <c>Error</c> type with no reference to
/// <c>SharedKernel.Primitives</c> and still be recognized. A method returning plain
/// <c>string?</c> never matches regardless of its body, since <see langword="null"/> is entirely
/// legitimate there.
/// </para>
/// </remarks>
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
