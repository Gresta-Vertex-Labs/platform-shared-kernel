using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0042 — Fires when a non-constant expression (string interpolation, concatenation, or any other
/// expression the compiler cannot prove is a compile-time constant) is passed as the <c>sql</c>
/// argument to a <c>SharedKernel.Persistence.Dapper.ReadModels.DapperReadService</c>/
/// <c>DapperCommandService</c> query or command method, or to a raw Dapper <c>SqlMapper</c> extension
/// method.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The gap.</strong> Every query/command method on <c>DapperReadService</c>/
/// <c>DapperCommandService</c>, and every Dapper <c>SqlMapper</c> extension method
/// (<c>QueryAsync</c>, <c>ExecuteAsync</c>, ...), takes its SQL as a plain <see cref="string"/>
/// parameter named <c>sql</c>. Nothing in the C# type system stops a caller from building that
/// string with <c>$"...{userInput}..."</c> or <c>"..." + userInput</c> instead of a parameterized
/// placeholder — the method compiles identically either way, and the difference only shows up as a
/// SQL-injection vulnerability at runtime, against whichever value happened to reach the interpolated
/// hole. <c>06.Persistence/CLAUDE.md</c>'s own "parameterized queries only" rule was, until this
/// analyzer, prose with no compiler enforcement behind it.
/// </para>
/// <para>
/// <strong>What is flagged.</strong> An argument passed to the <c>sql</c> parameter of a matching
/// method is flagged unless the compiler can prove it is a compile-time constant (a string literal,
/// a <see langword="const"/> field/local, or a concatenation of only such constants —
/// <see cref="SemanticModel.GetConstantValue(SyntaxNode, System.Threading.CancellationToken)"/>
/// returns a value for exactly this set). An <see cref="InterpolatedStringExpressionSyntax"/> is
/// never a compile-time constant and is always flagged; so is a plain local variable holding SQL text
/// built earlier by concatenation, since the analyzer only proves constancy at the argument
/// expression itself, not by tracing prior assignments.
/// </para>
/// <para>
/// <strong>Matched call sites.</strong> Any invocation whose target method declares a
/// <see langword="string"/> parameter literally named <c>sql</c>, where the containing type is
/// <c>SharedKernel.Persistence.Dapper.ReadModels.DapperReadService</c>,
/// <c>SharedKernel.Persistence.Dapper.ReadModels.DapperCommandService</c> (including an inherited
/// call through a subclass — the target method symbol resolves to the base declaration either way),
/// or <c>Dapper.SqlMapper</c> itself (the raw extension methods, for a caller that bypasses the base
/// classes and calls Dapper directly against an <c>IDbConnection</c>/<c>DbConnection</c>).
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0042</c> immediately around the call, with an
/// inline comment justifying why the argument is safe despite not being a compile-time constant (for
/// example, a value drawn from a fixed, developer-controlled allow-list rather than external input).
/// Prefer restructuring the call to use a <see langword="const"/>/literal SQL string with a genuine
/// parameter placeholder instead of suppressing.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonConstantDapperSqlArgumentAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0042";
    private const string SqlParameterName = "sql";
    private const string DapperReadServiceTypeName = "SharedKernel.Persistence.Dapper.ReadModels.DapperReadService";
    private const string DapperCommandServiceTypeName = "SharedKernel.Persistence.Dapper.ReadModels.DapperCommandService";
    private const string SqlMapperTypeName = "Dapper.SqlMapper";

    /// <summary>The diagnostic descriptor for SK0042.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Non-constant SQL argument passed to a Dapper query/command method",
        messageFormat: "The 'sql' argument passed to '{0}' is not a compile-time constant. Build SQL "
            + "from literal/const text only and pass values through parameters — string "
            + "interpolation or concatenation here is a SQL-injection vulnerability.",
        category: Security,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0042-nonconstantdappersqlargument"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method)
        {
            return;
        }

        if (!IsMatchedContainingType(method.ContainingType))
            return;

        var sqlParameter = FindSqlParameter(method);
        if (sqlParameter is null)
            return;

        var sqlArgument = FindArgumentExpression(invocation, method, sqlParameter);
        if (sqlArgument is null)
            return;

        // An interpolated string is never a compile-time constant, but check it first for a clearer
        // diagnostic location/consistency — GetConstantValue already returns HasValue=false for it,
        // this branch changes nothing observable, it documents intent.
        if (sqlArgument is InterpolatedStringExpressionSyntax)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, sqlArgument.GetLocation(), method.Name));
            return;
        }

        var constantValue = context.SemanticModel.GetConstantValue(sqlArgument, context.CancellationToken);
        if (!constantValue.HasValue)
            context.ReportDiagnostic(Diagnostic.Create(Rule, sqlArgument.GetLocation(), method.Name));
    }

    private static bool IsMatchedContainingType(INamedTypeSymbol? containingType)
    {
        for (var type = containingType; type is not null; type = type.BaseType)
        {
            var displayName = type.ToDisplayString();
            if (displayName is DapperReadServiceTypeName or DapperCommandServiceTypeName or SqlMapperTypeName)
                return true;
        }

        return false;
    }

    private static IParameterSymbol? FindSqlParameter(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type.SpecialType == SpecialType.System_String
                && string.Equals(parameter.Name, SqlParameterName, StringComparison.Ordinal))
            {
                return parameter;
            }
        }

        return null;
    }

    // Resolves the ExpressionSyntax bound to `sqlParameter` for this call — positional or named,
    // and never the default value of an omitted optional parameter (a `sql` parameter is never
    // optional in practice, but this guards the shape regardless).
    private static ExpressionSyntax? FindArgumentExpression(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        IParameterSymbol sqlParameter)
    {
        var argumentList = invocation.ArgumentList.Arguments;
        int positionalIndex = 0;

        foreach (var argument in argumentList)
        {
            if (argument.NameColon is not null)
            {
                if (string.Equals(argument.NameColon.Name.Identifier.ValueText, sqlParameter.Name, StringComparison.Ordinal))
                    return argument.Expression;

                continue;
            }

            if (positionalIndex == sqlParameter.Ordinal)
                return argument.Expression;

            positionalIndex++;
        }

        return null;
    }
}
