namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>A model-issued request to invoke one tool call.</summary>
/// <remarks>
/// Tool execution is never performed by this contract — see <see cref="ISemanticKernel"/>'s type-level
/// remarks. The caller executes the call itself and constructs a <see cref="ToolCallResult"/>.
/// </remarks>
public sealed record ToolCallRequest
{
    /// <summary>Gets the model-assigned identifier correlating this call to its eventual result.</summary>
    public required string CallId { get; init; }

    /// <summary>Gets the name of the tool the model wants invoked.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the call arguments as a JSON document.</summary>
    public required string ArgumentsJson { get; init; }
}
