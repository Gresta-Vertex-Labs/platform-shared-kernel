using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0035 — Fires when a call to a <c>[LoggerMessage]</c>-attributed logging method passes classified
/// data (a member carrying a <c>Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute</c>,
/// or an object whose type declares one) to a parameter that log redaction will not mask.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What counts as classified.</strong> A property or field carrying any attribute whose type
/// is, or derives at any depth from, <c>Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute</c>
/// — for example the 23 <c>SharedKernel.DataPrivacy.Classification.*DataAttribute</c> types. The
/// exception is <c>NoDataClassificationAttribute</c> (and anything derived from it), which marks
/// data as not personal and never counts.
/// </para>
/// <para>
/// <strong>Why the receiving parameter matters.</strong> The logging source generator, with
/// <c>EnableRedaction()</c>, redacts a parameter that carries a classification attribute itself, and
/// honors member classifications on a parameter marked <c>[LogProperties]</c>. So the rule fires for
/// (1) a direct reference to a classified member passed to a parameter that is not classified, and
/// (2) an expression whose static type declares a classified member, passed to a parameter that is
/// neither classified nor <c>[LogProperties]</c> (the object is then formatted with
/// <c>ToString()</c>, bypassing redaction). <c>[LogProperties]</c> does not make shape (1) safe.
/// </para>
/// <para>
/// <strong>Exempt:</strong> an argument that is a direct call to a
/// <c>SharedKernel.DataPrivacy.Masking.PiiMasking</c> method or to any
/// <c>SharedKernel.DataPrivacy.Masking.Pseudonymizer</c> method; parameters typed
/// <c>Microsoft.Extensions.Logging.LogLevel</c> or <see cref="Exception"/> (or a derived type).
/// </para>
/// <para>
/// All types are resolved by fully-qualified metadata name, so the analyzer needs no reference to
/// the compliance or DataPrivacy packages and tests can declare stand-ins in the fixture itself.
/// A compilation without the <c>DataClassificationAttribute</c> base type gets no diagnostics.
/// </para>
/// <para>
/// <strong>Scope limit (documented, intentional):</strong> this is a pattern check, not data-flow
/// analysis. Only a DIRECT member reference or a direct masking/pseudonymizing call is recognized —
/// an intermediate local variable (<c>var x = entity.Email; logger.LogX(x);</c>) or a helper method
/// that reads a classified member and returns it unmasked is not traced (mirrors SK0028,
/// <c>HealthCheckTagIntegrityRules</c>, SK0032).
/// </para>
/// <para>
/// Introduced WO-076 P-476; retargeted to the Microsoft compliance model P-554.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnmaskedClassifiedDataLoggingAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0035";

    private const string LoggerMessageAttributeMetadataName =
        "Microsoft.Extensions.Logging.LoggerMessageAttribute";
    private const string LogPropertiesAttributeMetadataName =
        "Microsoft.Extensions.Logging.LogPropertiesAttribute";
    private const string LogLevelMetadataName = "Microsoft.Extensions.Logging.LogLevel";
    private const string ExceptionMetadataName = "System.Exception";
    private const string DataClassificationAttributeMetadataName =
        "Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute";
    private const string NoDataClassificationAttributeMetadataName =
        "Microsoft.Extensions.Compliance.Classification.NoDataClassificationAttribute";
    private const string PiiMaskingMetadataName = "SharedKernel.DataPrivacy.Masking.PiiMasking";
    private const string PseudonymizerMetadataName = "SharedKernel.DataPrivacy.Masking.Pseudonymizer";
    private const string AttributeSuffix = "Attribute";

    /// <summary>The diagnostic descriptor for SK0035.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Unmasked classified data reaches a logging call site",
        messageFormat: "'{0}' carries {1} and is passed to [LoggerMessage] parameter '{2}', which is not "
            + "classified. Mark the parameter with the same classification attribute so log redaction "
            + "masks it, or mask it with SharedKernel.DataPrivacy.PiiMasking first.",
        category: Security,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0035-unmaskedclassifieddataatloggingcallsite"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var compilation = compilationContext.Compilation;

            var loggerMessageAttributeType = compilation.GetTypeByMetadataName(
                LoggerMessageAttributeMetadataName
            );
            var dataClassificationAttributeType = compilation.GetTypeByMetadataName(
                DataClassificationAttributeMetadataName
            );

            // Nothing this rule can ever flag in this compilation.
            if (loggerMessageAttributeType is null || dataClassificationAttributeType is null)
                return;

            var resolvedTypes = new ResolvedTypes(
                loggerMessageAttributeType,
                dataClassificationAttributeType,
                compilation.GetTypeByMetadataName(NoDataClassificationAttributeMetadataName),
                compilation.GetTypeByMetadataName(LogPropertiesAttributeMetadataName),
                compilation.GetTypeByMetadataName(PiiMaskingMetadataName),
                compilation.GetTypeByMetadataName(PseudonymizerMetadataName),
                compilation.GetTypeByMetadataName(LogLevelMetadataName),
                compilation.GetTypeByMetadataName(ExceptionMetadataName)
            );

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, resolvedTypes),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, ResolvedTypes resolvedTypes)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);

        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        if (!HasAttribute(methodSymbol, resolvedTypes.LoggerMessageAttributeType))
            return;

        var parameters = methodSymbol.Parameters;
        var arguments = invocation.ArgumentList.Arguments;

        var count = Math.Min(parameters.Length, arguments.Count);

        for (var i = 0; i < count; i++)
        {
            var parameter = parameters[i];

            if (IsSpecialParameter(parameter, resolvedTypes))
                continue;

            // A classified parameter is redacted by the logging source generator.
            if (GetClassificationAttribute(parameter, resolvedTypes) is not null)
                continue;

            var argumentExpression = arguments[i].Expression;

            if (IsMaskingCall(context, argumentExpression, resolvedTypes))
                continue;

            var logProperties = HasAttribute(parameter, resolvedTypes.LogPropertiesAttributeType);

            var classification = ResolveClassification(
                context,
                argumentExpression,
                resolvedTypes,
                logProperties,
                out var member
            );

            if (classification is null || member is null)
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    argumentExpression.GetLocation(),
                    $"{member.ContainingType?.Name}.{member.Name}",
                    FormatAttributeName(classification),
                    parameter.Name
                )
            );
        }
    }

    private static bool IsSpecialParameter(IParameterSymbol parameter, ResolvedTypes resolvedTypes)
    {
        var type = parameter.Type;

        if (resolvedTypes.LogLevelType is not null
            && SymbolEqualityComparer.Default.Equals(type, resolvedTypes.LogLevelType))
        {
            return true;
        }

        return resolvedTypes.ExceptionType is not null && DerivesFromOrIs(type, resolvedTypes.ExceptionType);
    }

    private static bool DerivesFromOrIs(ITypeSymbol? type, INamedTypeSymbol candidateBaseType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, candidateBaseType))
                return true;
        }

        return false;
    }

    private static bool IsMaskingCall(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression,
        ResolvedTypes resolvedTypes
    )
    {
        if (resolvedTypes.PiiMaskingType is null && resolvedTypes.PseudonymizerType is null)
            return false;

        if (expression is not InvocationExpressionSyntax innerInvocation)
            return false;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(innerInvocation, context.CancellationToken);

        if (symbolInfo.Symbol is not IMethodSymbol method)
            return false;

        return SymbolEqualityComparer.Default.Equals(method.ContainingType, resolvedTypes.PiiMaskingType)
            || SymbolEqualityComparer.Default.Equals(method.ContainingType, resolvedTypes.PseudonymizerType);
    }

    /// <summary>
    /// Returns the classification attribute type reaching the log for
    /// <paramref name="argumentExpression"/>: shape (1) a direct classified member reference, or
    /// shape (2) — unless the parameter is <c>[LogProperties]</c> — an expression whose static type
    /// declares a classified member. Returns <see langword="null"/> when neither applies.
    /// </summary>
    private static INamedTypeSymbol? ResolveClassification(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax argumentExpression,
        ResolvedTypes resolvedTypes,
        bool parameterHasLogProperties,
        out ISymbol? member
    )
    {
        // Shape 1 — a direct property/field member reference.
        var symbolInfo = context.SemanticModel.GetSymbolInfo(argumentExpression, context.CancellationToken);

        if (symbolInfo.Symbol is IPropertySymbol or IFieldSymbol)
        {
            var classification = GetClassificationAttribute(symbolInfo.Symbol, resolvedTypes);

            if (classification is not null)
            {
                member = symbolInfo.Symbol;
                return classification;
            }
        }

        // Shape 2 — a whole object whose static type declares a classified member. [LogProperties]
        // makes the generator log members individually, honoring their classifications.
        if (!parameterHasLogProperties)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(argumentExpression, context.CancellationToken);

            if (typeInfo.Type is INamedTypeSymbol namedType)
            {
                foreach (var candidateMember in namedType.GetMembers())
                {
                    if (candidateMember is not (IPropertySymbol or IFieldSymbol))
                        continue;

                    var classification = GetClassificationAttribute(candidateMember, resolvedTypes);

                    if (classification is null)
                        continue;

                    member = candidateMember;
                    return classification;
                }
            }
        }

        member = null;
        return null;
    }

    private static INamedTypeSymbol? GetClassificationAttribute(ISymbol symbol, ResolvedTypes resolvedTypes)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;

            if (!DerivesFromOrIs(attributeClass, resolvedTypes.DataClassificationAttributeType))
                continue;

            if (resolvedTypes.NoDataClassificationAttributeType is not null
                && DerivesFromOrIs(attributeClass, resolvedTypes.NoDataClassificationAttributeType))
            {
                continue;
            }

            return attributeClass;
        }

        return null;
    }

    private static string FormatAttributeName(INamedTypeSymbol attributeType)
    {
        var name = attributeType.Name;

        if (name.Length > AttributeSuffix.Length && name.EndsWith(AttributeSuffix, StringComparison.Ordinal))
            name = name.Substring(0, name.Length - AttributeSuffix.Length);

        return "[" + name + "]";
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attributeType)
    {
        if (attributeType is null)
            return false;

        foreach (var attribute in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Snapshot of every metadata-name-resolved type this analyzer needs, computed once per
    /// <see cref="Compilation"/> inside the compilation-start action.
    /// </summary>
    private sealed class ResolvedTypes
    {
        public ResolvedTypes(
            INamedTypeSymbol loggerMessageAttributeType,
            INamedTypeSymbol dataClassificationAttributeType,
            INamedTypeSymbol? noDataClassificationAttributeType,
            INamedTypeSymbol? logPropertiesAttributeType,
            INamedTypeSymbol? piiMaskingType,
            INamedTypeSymbol? pseudonymizerType,
            INamedTypeSymbol? logLevelType,
            INamedTypeSymbol? exceptionType
        )
        {
            LoggerMessageAttributeType = loggerMessageAttributeType;
            DataClassificationAttributeType = dataClassificationAttributeType;
            NoDataClassificationAttributeType = noDataClassificationAttributeType;
            LogPropertiesAttributeType = logPropertiesAttributeType;
            PiiMaskingType = piiMaskingType;
            PseudonymizerType = pseudonymizerType;
            LogLevelType = logLevelType;
            ExceptionType = exceptionType;
        }

        public INamedTypeSymbol LoggerMessageAttributeType { get; }

        public INamedTypeSymbol DataClassificationAttributeType { get; }

        public INamedTypeSymbol? NoDataClassificationAttributeType { get; }

        public INamedTypeSymbol? LogPropertiesAttributeType { get; }

        public INamedTypeSymbol? PiiMaskingType { get; }

        public INamedTypeSymbol? PseudonymizerType { get; }

        public INamedTypeSymbol? LogLevelType { get; }

        public INamedTypeSymbol? ExceptionType { get; }
    }
}
