namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>The caller-supplied result of executing one <see cref="ToolCallRequest"/>.</summary>
public sealed record ToolCallResult
{
    /// <summary>Gets the <see cref="ToolCallRequest.CallId"/> this result answers.</summary>
    public required string CallId { get; init; }

    /// <summary>Gets the tool's result as a JSON document.</summary>
    public required string ResultJson { get; init; }

    /// <summary>
    /// Converts this result into a <see cref="ChatMessage"/> (<see cref="ChatRole.Tool"/>, with
    /// <see cref="ChatMessage.Name"/> set to <see cref="CallId"/> and <see cref="ChatMessage.Content"/>
    /// set to <see cref="ResultJson"/>) suitable for appending to a follow-up
    /// <see cref="CompletionRequest.Messages"/> list. Convenience only.
    /// </summary>
    public ChatMessage ToMessage() => new()
    {
        Role = ChatRole.Tool,
        Name = CallId,
        Content = ResultJson,
    };
}
