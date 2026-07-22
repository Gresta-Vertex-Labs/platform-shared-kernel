namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>One message in a chat/completion conversation.</summary>
/// <remarks>
/// <b>Content is untrusted, sensitive, and never logged by default:</b> sending a payload to a hosted
/// model endpoint is an outbound transfer of whatever it contains; a message assembled from retrieved
/// vector-store content is untrusted input (the prompt-injection class). This layer provides plumbing,
/// not a sanitizer. <see cref="Content"/> must never appear as a log-message parameter.
/// </remarks>
public sealed record ChatMessage
{
    /// <summary>Gets the role this message was authored under.</summary>
    public required ChatRole Role { get; init; }

    /// <summary>Gets the message content. Never log this value — see the type-level remarks.</summary>
    public required string Content { get; init; }

    /// <summary>Gets the tool/function name attribution, or <see langword="null"/> when not applicable.</summary>
    public string? Name { get; init; }
}
