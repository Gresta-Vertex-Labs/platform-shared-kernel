using SharedKernel.Primitives.Results;

namespace SharedKernel.DataPrivacy.DataSubjectRequests;

/// <summary>
/// Handles a data subject's export or erasure request (GDPR "right to access"/"right to
/// erasure", KVKK's equivalent) against ONE service's own data.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by each consuming service against its own data. <c>SharedKernel.DataPrivacy</c>
/// ships no default implementation and no reflection-based generic one — a service's data shape
/// is entirely its own, and there is no honest way to export or erase "everything about a subject"
/// without knowing what that service actually stores.
/// </para>
/// <para>
/// CROSS-SERVICE ERASURE ORCHESTRATION IS EXPLICITLY OUT OF SCOPE FOR THIS PACKAGE. Coordinating
/// a single data-subject request across every service that might hold data about that subject —
/// calling each service's own <see cref="IDataSubjectRequestHandler"/> in turn — is a plausible
/// future composition built on top of this contract (a <c>19.Scheduling</c> job, or a
/// <c>17.Workflows</c> durable workflow, once real per-service handlers exist), but it is not
/// something this package attempts, ships, or assumes.
/// </para>
/// </remarks>
public interface IDataSubjectRequestHandler
{
    /// <summary>
    /// Exports every record this service holds about the given data subject.
    /// </summary>
    /// <param name="subjectId">The data subject's identifier, as known to this service.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A successful <see cref="Result{T}"/> carrying the exported
    /// <see cref="DataSubjectExportBundle"/>, or a failure <see cref="Result{T}"/> describing why
    /// the export could not be produced (e.g. the subject is unknown to this service).
    /// </returns>
    Task<Result<DataSubjectExportBundle>> ExportDataAsync(string subjectId, CancellationToken ct = default);

    /// <summary>
    /// Erases (or irreversibly anonymizes) every record this service holds about the given data
    /// subject.
    /// </summary>
    /// <param name="subjectId">The data subject's identifier, as known to this service.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A successful <see cref="Result{T}"/> carrying a <see cref="DataSubjectErasureReceipt"/>
    /// describing what was erased, or a failure <see cref="Result{T}"/> describing why the erasure
    /// could not be completed (e.g. a legal-hold or retention obligation blocks it).
    /// </returns>
    Task<Result<DataSubjectErasureReceipt>> RequestErasureAsync(string subjectId, CancellationToken ct = default);
}
