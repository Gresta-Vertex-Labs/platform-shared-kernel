using SharedKernel.Application.Commands;
using SharedKernel.Application.Pipeline.Commands;

namespace SharedKernel.Application.Pipeline.Tests.Support;

/// <summary>
/// A minimal <see cref="ICommandScope"/> double for isolated behavior unit tests that only need to
/// control <see cref="IsNested"/> — composed pipeline order is proven separately through the real
/// internal <c>CommandScope</c> via <c>AddSharedKernelApplication</c>.
/// </summary>
internal sealed class FakeCommandScope(bool isNested = false) : ICommandScope
{
    public List<Func<CancellationToken, Task>> Callbacks { get; } = [];

    public bool IsActive => true;

    public bool IsNested => isNested;

    public void OnCompleted(Func<CancellationToken, Task> callback) => Callbacks.Add(callback);
}
