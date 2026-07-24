using OpenAI;

namespace SharedKernel.AI.SemanticKernel.Raw;

/// <summary>The sole implementation of <see cref="IKernelRawClientAccessor"/>.</summary>
internal sealed class KernelRawClientAccessor : IKernelRawClientAccessor
{
    public KernelRawClientAccessor(OpenAIClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        Client = client;
    }

    public OpenAIClient Client { get; }
}
