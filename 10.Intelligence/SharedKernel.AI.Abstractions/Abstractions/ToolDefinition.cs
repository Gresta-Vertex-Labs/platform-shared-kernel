namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>The declaration of one callable tool/function offered to a completion call.</summary>
/// <remarks>
/// <b><see cref="ParametersJsonSchema"/> is a plain string, not a reflected <see cref="Type"/>:</b> the
/// caller supplies a JSON Schema document as text. This keeps
/// <c>SharedKernel.AI.Abstractions</c> reflection-free and AOT-clean — the reflection-heavy work of
/// turning a C# delegate/<see cref="Type"/> into a schema stays entirely inside
/// <c>SharedKernel.AI.SemanticKernel</c>, never touching this neutral contract.
/// </remarks>
public sealed record ToolDefinition
{
    /// <summary>Gets the tool name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the tool description.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the tool's parameters as a JSON Schema document.</summary>
    public required string ParametersJsonSchema { get; init; }
}
