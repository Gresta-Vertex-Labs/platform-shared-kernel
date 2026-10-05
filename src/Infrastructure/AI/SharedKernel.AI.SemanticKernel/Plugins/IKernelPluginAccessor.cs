using Microsoft.SemanticKernel;

namespace SharedKernel.AI.SemanticKernel.Plugins;

/// <summary>
/// A SemanticKernel-exclusive accessor onto a genuine <see cref="Microsoft.SemanticKernel.Kernel"/>
/// instance — declared only in this package, never <c>SharedKernel.AI.Abstractions</c>, since
/// SK's plugin/planner model (<see cref="KernelPlugin"/>, <c>FunctionChoiceBehavior</c>-driven
/// planning) is unique to this connector family and has no honest Qdrant/Milvus equivalent.
/// </summary>
/// <remarks>
/// The exposed <see cref="Kernel"/> is wired with the same underlying chat-completion service this
/// package's own <c>ISemanticKernel</c> implementation uses, but is a fully independent SK
/// <see cref="Microsoft.SemanticKernel.Kernel"/> object — importing a plugin here and invoking it
/// through the kernel's own <c>InvokeAsync</c> is genuine SK plugin execution, entirely separate from
/// and never required by <c>ISemanticKernel.CompleteAsync</c>'s own tool-calling contract (which never
/// executes tools itself).
/// </remarks>
public interface IKernelPluginAccessor
{
    /// <summary>Gets the underlying <see cref="Microsoft.SemanticKernel.Kernel"/> instance.</summary>
    Kernel Kernel { get; }
}
