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
/// In-memory test double for <see cref="IDataSubjectRequestHandler"/>. Records every request and
/// returns a configurable outcome per subject.
/// </summary>
/// <remarks>
/// <para>
/// Unconfigured subjects get what the contract prescribes for a subject the service knows nothing
/// about: an empty export, and a receipt with nothing erased and nothing retained. Like a real
/// handler, it returns the first outcome again when a <see cref="DataSubjectRequest.RequestId"/> is repeated.
/// </para>
/// <para>
/// Timestamps come from the injected <see cref="IClock"/>, a fresh <see cref="FakeClock"/> by default.
/// </para>
/// </remarks>
public sealed class RecordingDataSubjectRequestHandler : IDataSubjectRequestHandler
{
    private readonly IClock _clock;
    private readonly string _source;
    private readonly ConcurrentQueue<DataSubjectRequest> _exportRequests = new();
    private readonly ConcurrentQueue<DataSubjectRequest> _erasureRequests = new();
    private readonly ConcurrentDictionary<string, SharedKernel.Primitives.Results.Result<DataSubjectExport>> _exportResults = new();
    private readonly ConcurrentDictionary<string, SharedKernel.Primitives.Results.Result<DataSubjectErasureReceipt>> _erasureResults = new();
    private readonly ConcurrentDictionary<string, SharedKernel.Primitives.Results.Result<DataSubjectExport>> _completedExports = new();
    private readonly ConcurrentDictionary<string, SharedKernel.Primitives.Results.Result<DataSubjectErasureReceipt>> _completedErasures = new();

    /// <summary>Initializes a new instance of the <see cref="RecordingDataSubjectRequestHandler"/> class.</summary>
    /// <param name="clock">The clock for export and completion times; a fresh <see cref="FakeClock"/> when omitted.</param>
    /// <param name="source">The service name written to every export and receipt.</param>
    public RecordingDataSubjectRequestHandler(IClock? clock = null, string source = "test-service")
    {
        _clock = clock ?? new FakeClock();
        _source = source;
    }

    /// <summary>Gets every export request, in call order, including repeats.</summary>
    public IReadOnlyList<DataSubjectRequest> ExportRequests => [.. _exportRequests];

    /// <summary>Gets every erasure request, in call order, including repeats.</summary>
    public IReadOnlyList<DataSubjectRequest> ErasureRequests => [.. _erasureRequests];

    /// <summary>Sets the outcome <see cref="ExportAsync"/> returns for <paramref name="subjectId"/>.</summary>
    /// <param name="subjectId">The subject.</param>
    /// <param name="result">The outcome.</param>
    public void SetExportResult(string subjectId, SharedKernel.Primitives.Results.Result<DataSubjectExport> result)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        _exportResults[subjectId] = result;
    }

    /// <summary>Sets the outcome <see cref="EraseAsync"/> returns for <paramref name="subjectId"/>.</summary>
    /// <param name="subjectId">The subject.</param>
    /// <param name="result">The outcome.</param>
    public void SetErasureResult(string subjectId, SharedKernel.Primitives.Results.Result<DataSubjectErasureReceipt> result)
    {
        ArgumentNullException.ThrowIfNull(subjectId);
        _erasureResults[subjectId] = result;
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<DataSubjectExport>> ExportAsync(DataSubjectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _exportRequests.Enqueue(request);

        var result = _completedExports.GetOrAdd(request.RequestId, _ => _exportResults.TryGetValue(request.SubjectId, out var configured)
            ? configured
            : new DataSubjectExport(request, _source, _clock.UtcNow, []));

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<DataSubjectErasureReceipt>> EraseAsync(DataSubjectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _erasureRequests.Enqueue(request);

        var result = _completedErasures.GetOrAdd(request.RequestId, _ => _erasureResults.TryGetValue(request.SubjectId, out var configured)
            ? configured
            : new DataSubjectErasureReceipt(request, _source, _clock.UtcNow, 0, 0, []));

        return Task.FromResult(result);
    }

    /// <summary>Asserts that <see cref="ExportAsync"/> was called for <paramref name="subjectId"/>.</summary>
    /// <param name="subjectId">The subject.</param>
    /// <exception cref="InvalidOperationException">No export request was recorded for the subject.</exception>
    public void ShouldHaveExported(string subjectId)
    {
        if (!_exportRequests.Any(r => r.SubjectId == subjectId))
        {
            throw new InvalidOperationException($"Expected an export request for subject '{subjectId}' but none was found.");
        }
    }

    /// <summary>Asserts that <see cref="EraseAsync"/> was called for <paramref name="subjectId"/>.</summary>
    /// <param name="subjectId">The subject.</param>
    /// <exception cref="InvalidOperationException">No erasure request was recorded for the subject.</exception>
    public void ShouldHaveErased(string subjectId)
    {
        if (!_erasureRequests.Any(r => r.SubjectId == subjectId))
        {
            throw new InvalidOperationException($"Expected an erasure request for subject '{subjectId}' but none was found.");
        }
    }
}
