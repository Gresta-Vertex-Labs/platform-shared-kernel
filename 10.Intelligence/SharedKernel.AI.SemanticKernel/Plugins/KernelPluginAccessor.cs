using Microsoft.SemanticKernel;

namespace SharedKernel.AI.SemanticKernel.Plugins;

/// <summary>The sole implementation of <see cref="IKernelPluginAccessor"/>.</summary>
internal sealed class KernelPluginAccessor : IKernelPluginAccessor
{
    public KernelPluginAccessor(Kernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        Kernel = kernel;
    }

    public Kernel Kernel { get; }
}
