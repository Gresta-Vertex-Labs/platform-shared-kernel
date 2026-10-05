using System.Runtime.CompilerServices;

namespace SharedKernel.Reporting.Pdf.Tests;

/// <summary>
/// An <see cref="IAsyncEnumerable{T}"/> that throws if enumerated more than once — proves a
/// provider under test streams the source in a single pass rather than materializing it.
/// </summary>
internal sealed class SingleEnumerationAsyncEnumerable<T>(IReadOnlyList<T> items) : IAsyncEnumerable<T>
{
    private int _enumerationCount;

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _enumerationCount) > 1)
        {
            throw new InvalidOperationException("Source was enumerated more than once — the provider under test buffered it instead of streaming in a single pass.");
        }

        return Enumerate(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    private async IAsyncEnumerable<T> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }
}
