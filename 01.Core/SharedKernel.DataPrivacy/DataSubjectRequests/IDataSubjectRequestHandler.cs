using SharedKernel.Primitives.Results;

namespace SharedKernel.DataPrivacy.DataSubjectRequests;

/// <summary>
/// Exports or erases what ONE service holds about a data subject. Each service implements it
/// against its own data.
/// </summary>
/// <remarks>
/// <para>
/// No default implementation ships: there is no honest generic way to find "everything about a
/// person" without knowing what a service stores. Calling every service's handler for one request
/// is the job of an orchestrator (a <c>17.Workflows</c> workflow or a <c>19.Scheduling</c> job),
/// which this package does not provide.
/// </para>
/// <para>Rules for an implementation:</para>
/// <list type="bullet">
/// <item><description>
/// A subject this service knows nothing about is a success with nothing in it, never a failure, so
/// an orchestrator can send every request to every service.
/// </description></item>
/// <item><description>
/// The same <see cref="DataSubjectRequest.RequestId"/> again returns the first outcome.
/// </description></item>
/// <item><description>
/// Data the service must keep (invoices kept for tax law, a legal hold) is reported in
/// <see cref="DataSubjectErasureReceipt.Retained"/> with its legal basis; the rest is still erased.
/// </description></item>
/// <item><description>
/// A failure <see cref="Result{T}"/> means the request could not be carried out now and should be
/// retried or escalated, never that it was refused for legal reasons; see
/// <see cref="DataPrivacyErrorCodes"/>.
/// </description></item>
/// </list>
/// </remarks>
public interface IDataSubjectRequestHandler
{
    /// <summary>Exports what this service holds about the request's data subject (right of access and portability).</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The export, or the reason it could not be produced.</returns>
    Task<Result<DataSubjectExport>> ExportAsync(DataSubjectRequest request, CancellationToken cancellationToken = default);

    /// <summary>Erases or irreversibly anonymizes what this service holds about the request's data subject (right to erasure).</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>What was erased, anonymized and kept, or the reason the erasure could not be carried out.</returns>
    Task<Result<DataSubjectErasureReceipt>> EraseAsync(DataSubjectRequest request, CancellationToken cancellationToken = default);
}
