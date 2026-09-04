namespace SharedKernel.DataPrivacy.DataSubjectRequests;

/// <summary>
/// The result of a successful <see cref="IDataSubjectRequestHandler.ExportDataAsync"/> call —
/// every record one service holds about a data subject, as of the moment the export ran.
/// </summary>
/// <param name="SubjectId">The data subject's identifier, as known to the exporting service.</param>
/// <param name="ExportedAtUtc">The instant the export was produced.</param>
/// <param name="Data">
/// The exported data, keyed by a caller-defined field/record name. Each implementing service
/// defines its own key naming and value shapes — this contract imposes no schema beyond "a flat,
/// serializable key/value view of what this service holds about the subject".
/// </param>
public sealed record DataSubjectExportBundle(
    string SubjectId,
    DateTimeOffset ExportedAtUtc,
    IReadOnlyDictionary<string, object?> Data);
