using SharedKernel.Application.Commands;
using SharedKernel.Application.Pipeline.Commands;

namespace SharedKernel.Application.Pipeline.Caching.Tests.Support;

/// <summary>A minimal <see cref="ICommandScope"/> double recording every registered callback.</summary>
internal sealed class FakeCommandScope : ICommandScope
{
    public List<Func<CancellationToken, Task>> Callbacks { get; } = [];

    public bool IsActive => true;
    public bool IsNested => false;

    public void OnCompleted(Func<CancellationToken, Task> callback) => Callbacks.Add(callback);

    public async Task RunCallbacksAsync(CancellationToken cancellationToken = default)
    {
        foreach (var callback in Callbacks)
            await callback(cancellationToken).ConfigureAwait(false);
    }
}
