namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>The role a <see cref="ChatMessage"/> was authored under.</summary>
public enum ChatRole
{
    /// <summary>A system/instruction message.</summary>
    System = 0,

    /// <summary>A user-authored message.</summary>
    User = 1,

    /// <summary>An assistant-authored message.</summary>
    Assistant = 2,

    /// <summary>A tool-result message.</summary>
    Tool = 3,
}
