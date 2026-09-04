using System.Collections.Concurrent;
using SharedKernel.DataPrivacy.DataSubjectRequests;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
// NOTE: Result<T>/Error are deliberately NEVER `using`-imported bare in this file — this project's
// HotChocolate.Data reference pulls in a global `using GreenDonut;`/`using HotChocolate;`, and
// GreenDonut.Result<TValue>/HotChocolate.Error collide with
// SharedKernel.Primitives.Results.Result<T>/SharedKernel.Primitives.Errors.Error (CS0104). Every
// use below is fully qualified instead, mirroring the established
// Cryptography/FakeEnvelopeEncryptionProvider.cs precedent for this same project-wide ambiguity.

namespace SharedKernel.Testing.DataPrivacy;

/// <summary>
/// In-memory test double for <see cref="IDataSubjectRequestHandler"/>. Records every
/// <see cref="ExportDataAsync"/>/<see cref="RequestErasureAsync"/> call for later assertion,
/// returning a caller-configurable per-<c>subjectId</c> outcome.
/// </summary>
/// <remarks>
/// <para>
/// Defaults to a synthetic success for both operations — zero-configuration by default, mirroring
/// <c>FakeUserContext</c>'s "most test setups need zero configuration" convention — while
/// supporting BOTH success and failure outcomes per call via <see cref="SetExportResult"/>/
/// <see cref="SetErasureResult"/>.
/// </para>
/// <para>
/// Timestamps are stamped via an injected <see cref="IClock"/> (defaulting to a fresh
/// <see cref="FakeClock"/> for zero-config convenience), composable with a caller-supplied
/// <see cref="FakeClock"/> the same way every other clock-aware fake in this package is.
/// </para>
/// </remarks>
public sealed class RecordingDataSubjectRequestHandler : IDataSubjectRequestHandler
{
    private readonly IClock _clock;
    private readonly ConcurrentQueue<string> _exportRequests = new();
    private readonly ConcurrentQueue<string> _erasureRequests = new();
    private readonly ConcurrentDictionary<string, SharedKernel.Primitives.Results.Result<DataSubjectExportBundle>> _exportResults = new();
    private readonly ConcurrentDictionary<string, SharedKernel.Primitives.Results.Result<DataSubjectErasureReceipt>> _erasureResults = new();

    /// <summary>Initialises a new <see cref="RecordingDataSubjectRequestHandler"/>.</summary>
    /// <param name="clock">
    /// The clock to stamp <see cref="DataSubjectExportBundle.ExportedAtUtc"/>/
    /// <see cref="DataSubjectErasureReceipt.ErasedAtUtc"/> from. Defaults to a fresh
    /// <see cref="FakeClock"/> when omitted.
    /// </param>
    public RecordingDataSubjectRequestHandler(IClock? clock = null) => _clock = clock ?? new FakeClock();

    /// <summary>Every <c>subjectId</c> recorded via <see cref="ExportDataAsync"/>, in call order.</summary>
    public IReadOnlyList<string> ExportRequests => [.. _exportRequests];

    /// <summary>Every <c>subjectId</c> recorded via <see cref="RequestErasureAsync"/>, in call order.</summary>
    public IReadOnlyList<string> ErasureRequests => [.. _erasureRequests];

    /// <summary>
    /// Configures the result <see cref="ExportDataAsync"/> returns for <paramref name="subjectId"/>,
    /// overriding the default synthetic success.
    /// </summary>
    public void SetExportResult(string subjectId, SharedKernel.Primitives.Results.Result<DataSubjectExportBundle> result)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        _exportResults[subjectId] = result;
    }

    /// <summary>
    /// Configures the result <see cref="RequestErasureAsync"/> returns for
    /// <paramref name="subjectId"/>, overriding the default synthetic success.
    /// </summary>
    public void SetErasureResult(string subjectId, SharedKernel.Primitives.Results.Result<DataSubjectErasureReceipt> result)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        _erasureResults[subjectId] = result;
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<DataSubjectExportBundle>> ExportDataAsync(string subjectId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        _exportRequests.Enqueue(subjectId);

        var result = _exportResults.TryGetValue(subjectId, out var configured)
            ? configured
            : SharedKernel.Primitives.Results.Result<DataSubjectExportBundle>.Success(
                new DataSubjectExportBundle(subjectId, _clock.UtcNow, new Dictionary<string, object?>()));

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<DataSubjectErasureReceipt>> RequestErasureAsync(string subjectId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        _erasureRequests.Enqueue(subjectId);

        var result = _erasureResults.TryGetValue(subjectId, out var configured)
            ? configured
            : SharedKernel.Primitives.Results.Result<DataSubjectErasureReceipt>.Success(new DataSubjectErasureReceipt(subjectId, _clock.UtcNow, 0));

        return Task.FromResult(result);
    }

    /// <summary>Asserts that <see cref="ExportDataAsync"/> was called for <paramref name="subjectId"/>.</summary>
    /// <exception cref="InvalidOperationException">No matching export request was recorded.</exception>
    public void ShouldHaveExported(string subjectId)
    {
        if (!_exportRequests.Contains(subjectId))
        {
            throw new InvalidOperationException($"Expected an export request for subject '{subjectId}' but none was found.");
        }
    }

    /// <summary>Asserts that <see cref="RequestErasureAsync"/> was called for <paramref name="subjectId"/>.</summary>
    /// <exception cref="InvalidOperationException">No matching erasure request was recorded.</exception>
    public void ShouldHaveErased(string subjectId)
    {
        if (!_erasureRequests.Contains(subjectId))
        {
            throw new InvalidOperationException($"Expected an erasure request for subject '{subjectId}' but none was found.");
        }
    }
}
