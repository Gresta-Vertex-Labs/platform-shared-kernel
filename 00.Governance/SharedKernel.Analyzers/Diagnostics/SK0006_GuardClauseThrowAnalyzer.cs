using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0006 — Fires when a method declared on a type that implements
/// <c>SharedKernel.Guards.Clauses.IGuardClause</c> contains a <see langword="throw"/>
/// statement or throw expression, outside the <c>Guard.Throw</c> companion class.
/// </summary>
/// <remarks>
/// The SharedKernel guard system uses a two-path contract:
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Functional path</b> — <c>Guard.Against.*</c> extension methods must be pure:
///     they return <c>Error?</c> and must never throw.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Imperative path</b> — <c>Guard.Throw</c> is the only place where throwing is
///     permitted. This analyzer excludes any method declared in the <c>Guard.Throw</c>
///     class (containing type name is <c>Throw</c> nested inside <c>Guard</c>).
///     </description>
///   </item>
/// </list>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GuardClauseThrowAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0006";

    // Full metadata name of the IGuardClause interface
    private const string IGuardClauseFullName = "SharedKernel.Guards.Clauses.IGuardClause";

    // The Guard.Throw companion class: its containing type is named "Guard" and its own name is "Throw"
    private const string GuardThrowTypeName = "Throw";
    private const string GuardContainingTypeName = "Guard";

    /// <summary>The diagnostic descriptor for SK0006.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Guard clause functional-path method must not throw",
        messageFormat: "Method '{0}' on type '{1}' implements IGuardClause but contains a throw — use the functional path (return Error?) instead, or move throw behavior to the Guard.Throw companion class",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0006-guardclausethrow"
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
            AnalyzeThrowStatement,
            SyntaxKind.ThrowStatement
        );

        context.RegisterSyntaxNodeAction(
            AnalyzeThrowExpression,
            SyntaxKind.ThrowExpression
        );
    }

    private static void AnalyzeThrowStatement(SyntaxNodeAnalysisContext context)
    {
        var throwStatement = (ThrowStatementSyntax)context.Node;
        AnalyzeThrow(context, throwStatement, throwStatement.GetLocation());
    }

    private static void AnalyzeThrowExpression(SyntaxNodeAnalysisContext context)
    {
        var throwExpression = (ThrowExpressionSyntax)context.Node;
        AnalyzeThrow(context, throwExpression, throwExpression.GetLocation());
    }

    private static void AnalyzeThrow(
        SyntaxNodeAnalysisContext context,
        SyntaxNode throwNode,
        Location location)
    {
        // Walk up the syntax tree to find the containing method and its declaring type
        var containingMethod = GetContainingMethodOrAccessor(throwNode);
        if (containingMethod is null)
            return;

        var containingType = GetContainingTypeDeclaration(containingMethod);
        if (containingType is null)
            return;

        // Resolve the containing type's symbol
        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(containingType) as INamedTypeSymbol;
        if (typeSymbol is null)
            return;

        // Exclusion: skip methods declared in the Guard.Throw companion class
        if (IsGuardThrowClass(typeSymbol))
            return;

        // Determine whether this method is part of the IGuardClause contract:
        // Case 1 — the declaring type directly implements IGuardClause (instance methods)
        // Case 2 — the method is an extension method whose first parameter (this) is IGuardClause
        bool isGuardMethod = ImplementsIGuardClause(typeSymbol)
            || IsExtensionMethodOnIGuardClause(context, containingMethod);

        if (!isGuardMethod)
            return;

        var methodName = GetMethodName(containingMethod);

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, location, methodName, typeSymbol.Name)
        );
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="methodNode"/> is a static extension
    /// method whose first parameter (the <c>this</c> parameter) has a type that is
    /// <c>IGuardClause</c> or implements <c>IGuardClause</c>.
    /// </summary>
    private static bool IsExtensionMethodOnIGuardClause(
        SyntaxNodeAnalysisContext context,
        SyntaxNode methodNode)
    {
        if (methodNode is not MethodDeclarationSyntax methodDecl)
            return false;

        // Must be static and have parameters
        if (!methodDecl.Modifiers.Any(SyntaxKind.StaticKeyword))
            return false;

        if (methodDecl.ParameterList.Parameters.Count == 0)
            return false;

        var firstParam = methodDecl.ParameterList.Parameters[0];

        // Must have 'this' modifier
        if (!firstParam.Modifiers.Any(SyntaxKind.ThisKeyword))
            return false;

        // Resolve the parameter type
        if (firstParam.Type is null)
            return false;

        var paramTypeInfo = context.SemanticModel.GetTypeInfo(firstParam.Type);
        var paramType = paramTypeInfo.Type as INamedTypeSymbol;
        if (paramType is null)
            return false;

        // Check if the parameter type IS IGuardClause or implements it
        if (GetFullMetadataName(paramType) == IGuardClauseFullName)
            return true;

        return ImplementsIGuardClause(paramType);
    }

    /// <summary>
    /// Walks the syntax tree upward to find the nearest containing method, local function,
    /// or accessor. Returns <see langword="null"/> if none is found.
    /// </summary>
    private static SyntaxNode? GetContainingMethodOrAccessor(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is MethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AccessorDeclarationSyntax
                or ConstructorDeclarationSyntax
                or DestructorDeclarationSyntax
                or OperatorDeclarationSyntax
                or ConversionOperatorDeclarationSyntax)
            {
                return current;
            }

            // Stop at type boundary — don't cross into an outer type's method
            if (current is TypeDeclarationSyntax)
                return null;

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Returns the nearest containing <see cref="TypeDeclarationSyntax"/> ancestor of
    /// <paramref name="methodNode"/>.
    /// </summary>
    private static TypeDeclarationSyntax? GetContainingTypeDeclaration(SyntaxNode methodNode)
    {
        var current = methodNode.Parent;
        while (current is not null)
        {
            if (current is TypeDeclarationSyntax typeDecl)
                return typeDecl;

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="typeSymbol"/> is the
    /// <c>Guard.Throw</c> companion class — i.e., its name is <c>Throw</c> and its
    /// containing type is named <c>Guard</c>.
    /// </summary>
    private static bool IsGuardThrowClass(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.Name == GuardThrowTypeName
            && typeSymbol.ContainingType?.Name == GuardContainingTypeName;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="typeSymbol"/> or any of its base
    /// types/interfaces directly implement <c>SharedKernel.Guards.Clauses.IGuardClause</c>.
    /// </summary>
    private static bool ImplementsIGuardClause(INamedTypeSymbol typeSymbol)
    {
        // Check all interfaces in the flattened interface list
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            if (GetFullMetadataName(iface) == IGuardClauseFullName)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the full metadata name of a type symbol using dot-notation for namespaces
    /// and plus-notation for nested types.
    /// </summary>
    private static string GetFullMetadataName(INamedTypeSymbol symbol)
    {
        if (symbol.ContainingType is not null)
            return $"{GetFullMetadataName(symbol.ContainingType)}+{symbol.MetadataName}";

        if (symbol.ContainingNamespace is null || symbol.ContainingNamespace.IsGlobalNamespace)
            return symbol.MetadataName;

        return $"{symbol.ContainingNamespace.ToDisplayString()}.{symbol.MetadataName}";
    }

    /// <summary>Returns the name of the method for use in the diagnostic message.</summary>
    private static string GetMethodName(SyntaxNode methodNode) =>
        methodNode switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            LocalFunctionStatementSyntax lf => lf.Identifier.Text,
            AccessorDeclarationSyntax acc => acc.Keyword.Text,
            ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
            DestructorDeclarationSyntax dtor => $"~{dtor.Identifier.Text}",
            OperatorDeclarationSyntax op => $"operator {op.OperatorToken.Text}",
            _ => "<unknown>"
        };
}
