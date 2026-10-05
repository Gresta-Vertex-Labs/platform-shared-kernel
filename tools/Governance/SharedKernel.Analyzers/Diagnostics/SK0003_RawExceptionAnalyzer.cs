using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0003 — Fires on a <see cref="ThrowStatementSyntax"/> or <see cref="ThrowExpressionSyntax"/>
/// whose thrown expression is an <see cref="ObjectCreationExpressionSyntax"/> resolving, via the
/// <see cref="SemanticModel"/>, to exactly <c>System.Exception</c> or
/// <c>System.ApplicationException</c> — never a subclass — with no argument resolving to a type
/// named <c>Error</c>.
/// </summary>
/// <remarks>
/// <para>
/// A bare <c>Exception</c>/<c>ApplicationException</c> carries no machine-readable error
/// information for <c>Result&lt;T&gt;.Failure(error)</c> or the platform's typed
/// <c>SharedKernelException</c> hierarchy to carry forward as an <c>Error</c> payload — that
/// information is simply gone the moment the raw type is thrown instead.
/// </para>
/// <para>
/// <strong>Two registrations, one handler.</strong> <see cref="SyntaxKind.ThrowStatement"/> covers
/// the ordinary <c>throw new Exception(...);</c> statement form; <see cref="SyntaxKind.ThrowExpression"/>
/// covers the expression form (<c>input ?? throw new ArgumentNullException(...)</c>). Both route
/// through the same <c>AnalyzeCreation</c> check, so the rule behaves identically regardless of
/// which C# throw shape is used — and, since <c>ArgumentNullException</c> is neither of the two
/// forbidden exact types, that particular pass-through example never fires anyway.
/// </para>
/// <para>
/// <strong>Exact-type match, not subclass.</strong> <c>IsForbiddenExceptionType</c> compares
/// <c>type.ToDisplayString()</c> against the two exact full names — a
/// <c>DomainException : Exception</c> subclass is never flagged by this rule, regardless of what it
/// is constructed with, even when it is itself constructed from a bare string with no <c>Error</c>
/// payload. That is deliberate: typed subclasses are the sanctioned path, and SK0005 separately
/// governs the shape of THEIR constructor arguments. SK0003's only job is to catch the raw,
/// untyped throw.
/// </para>
/// <para>
/// <strong>Error-argument escape hatch.</strong> If any argument to the <c>Exception</c>/
/// <c>ApplicationException</c> constructor resolves to a type simply named <c>Error</c>, the
/// diagnostic is suppressed — <c>HasErrorArgument</c> scans every argument's resolved type name,
/// not just a single expected position, so <c>new Exception(someError)</c> and
/// <c>new Exception("msg", someError)</c> both pass. In practice a genuinely
/// <c>Error</c>-carrying throw is expected to use the typed <c>SharedKernelException</c> hierarchy
/// instead; this escape hatch exists for the narrower case where a raw BCL exception type is used
/// but still does carry one.
/// </para>
/// </remarks>
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
