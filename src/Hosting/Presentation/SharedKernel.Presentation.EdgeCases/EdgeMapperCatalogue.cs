using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Communication;
using SharedKernel.Core.Exceptions;
using SharedKernel.Execution;
using SharedKernel.Execution.Transactions;
using SharedKernel.FeatureManagement;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Serialization;
using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Exceptions;
using SharedKernel.Storage;
using SharedKernel.Workflows.Temporal.Errors;

namespace SharedKernel.Presentation.EdgeCases;

/// <summary>
/// The edge-mapper conformance catalogue: every <see cref="SharedKernelException"/> the other domains throw or return,
/// with the type, code, HTTP status and gRPC status each protocol must answer. Codes come from the owners' public
/// constants, never from literals, so a rename there fails the suites that consume this catalogue. The one exception is
/// the workflow rows, whose owner exposes the codes only through <c>WorkflowErrors</c> (no public constants).
/// </summary>
/// <remarks>
/// Not covered, on purpose: <c>ReportAbortedException</c> (internal to <c>20.Reporting</c>) and
/// <c>InvalidReportDefinitionException</c>, which <c>20.Reporting</c> has not shipped yet.
/// </remarks>
public static class EdgeMapperCatalogue
{
    private const string Message = "An internal sentence that names a host, db-7.internal.";

    private static readonly IReadOnlyList<EdgeMapperCase> AllCases =
    [
        new("commit-outcome-unknown", () => new CommitOutcomeUnknownException(new TimeoutException()), ErrorType.Timeout, ExecutionErrorCodes.CommitOutcomeUnknown, 504, "DeadlineExceeded"),
        new("transaction-rolled-back", () => new TransactionRolledBackException(), ErrorType.Unexpected, ExecutionErrorCodes.TransactionRolledBack, 500, "Internal"),
        new("feature-flag-invalid", () => new FeatureFlagValidationException(["Flag 'x' is invalid."]), ErrorType.Unexpected, FeatureManagementErrorCodes.InvalidFlagConfiguration, 500, "Internal"),
        new("lock-unavailable", () => DistributedLockUnavailableException.ForResource("orders:42"), ErrorType.Unavailable, CachingErrorCodes.LockUnavailable, 503, "Unavailable"),
        new("concurrent-delivery", () => new ConcurrentMessageDeliveryException(new Guid("5c1e3b52-0000-4000-8000-000000000001"), typeof(string)), ErrorType.Conflict, MessagingErrorCodes.ConcurrentDelivery, 409, "Aborted"),
        new("payload-transform-mismatch", () => new PayloadTransformMismatchException(Error.Unexpected(MessagingErrorCodes.PayloadTransformMismatch, Message)), ErrorType.Unexpected, MessagingErrorCodes.PayloadTransformMismatch, 500, "Internal"),
        Storage("storage-unavailable", Error.Unavailable(StorageErrorCodes.Unavailable, Message), ErrorType.Unavailable, 503, "Unavailable"),
        Storage("storage-access-denied", Error.Forbidden(StorageErrorCodes.AccessDenied, Message), ErrorType.Forbidden, 403, "PermissionDenied"),
        Storage("storage-invalid-request", Error.Validation(StorageErrorCodes.InvalidRequest, Message), ErrorType.Validation, 400, "InvalidArgument"),
        Storage("storage-not-found", Error.NotFound(StorageErrorCodes.NotFound, Message), ErrorType.NotFound, 404, "NotFound"),
        Storage("storage-already-exists", Error.Conflict(StorageErrorCodes.AlreadyExists, Message), ErrorType.Conflict, 409, "Aborted"),
        Storage("storage-provider-error", Error.Unexpected(StorageErrorCodes.ProviderError, Message), ErrorType.Unexpected, 500, "Internal"),
        new("search-stream-unavailable", () => new SearchStreamException(Error.Unavailable(SearchErrorCodes.Unavailable, Message)), ErrorType.Unavailable, SearchErrorCodes.Unavailable, 503, "Unavailable"),
        new("search-stream-timeout", () => new SearchStreamException(Error.Timeout(SearchErrorCodes.Timeout, Message)), ErrorType.Timeout, SearchErrorCodes.Timeout, 504, "DeadlineExceeded"),
        new("search-stream-engine-unauthorized", () => new SearchStreamException(Error.Unexpected(SearchErrorCodes.EngineUnauthorized, Message)), ErrorType.Unexpected, SearchErrorCodes.EngineUnauthorized, 500, "Internal"),
        new("search-stream-engine-fault", () => new SearchStreamException(Error.Unexpected(SearchErrorCodes.EngineFault, Message)), ErrorType.Unexpected, SearchErrorCodes.EngineFault, 500, "Internal"),
        new("intelligence-stream-unavailable", () => new IntelligenceStreamException(Error.Unavailable(IntelligenceErrorCodes.Unavailable, Message)), ErrorType.Unavailable, IntelligenceErrorCodes.Unavailable, 503, "Unavailable"),
        new("intelligence-stream-timeout", () => new IntelligenceStreamException(Error.Timeout(IntelligenceErrorCodes.Timeout, Message)), ErrorType.Timeout, IntelligenceErrorCodes.Timeout, 504, "DeadlineExceeded"),
        new("intelligence-stream-engine-unauthorized", () => new IntelligenceStreamException(Error.Unexpected(IntelligenceErrorCodes.EngineUnauthorized, Message)), ErrorType.Unexpected, IntelligenceErrorCodes.EngineUnauthorized, 500, "Internal"),
        new("intelligence-stream-engine-fault", () => new IntelligenceStreamException(Error.Unexpected(IntelligenceErrorCodes.EngineFault, Message)), ErrorType.Unexpected, IntelligenceErrorCodes.EngineFault, 500, "Internal"),
        new("access-token-unavailable", () => new AccessTokenUnavailableException("inventory", Error.Unavailable(CommunicationErrorCodes.AccessTokenUnavailable, Message)), ErrorType.Unavailable, CommunicationErrorCodes.AccessTokenUnavailable, 503, "Unavailable"),
        new("idempotency-store-unavailable", () => new IdempotencyStoreUnavailableException(new TimeoutException()), ErrorType.Unavailable, ErrorCodes.Idempotency.StoreUnavailable, 503, "Unavailable"),
        new("field-encryption-failed", () => new FieldEncryptionException(Error.Unexpected(PersistenceErrorCodes.Encryption.DecryptionFailed, Message)), ErrorType.Unexpected, PersistenceErrorCodes.Encryption.DecryptionFailed, 500, "Internal"),
        new("persistence-statement-timeout", () => Error.Timeout(PersistenceErrorCodes.Postgresql.StatementTimeout, Message).ToException(), ErrorType.Timeout, PersistenceErrorCodes.Postgresql.StatementTimeout, 504, "DeadlineExceeded"),
        new("audit-record-not-found", () => Error.NotFound(PersistenceErrorCodes.Audit.RecordNotFound, Message).ToException(), ErrorType.NotFound, PersistenceErrorCodes.Audit.RecordNotFound, 404, "NotFound"),
        new("persistence-tenant-required", () => Error.Forbidden(PersistenceErrorCodes.Tenancy.TenantRequired, Message).ToException(), ErrorType.Forbidden, PersistenceErrorCodes.Tenancy.TenantRequired, 403, "PermissionDenied"),
        // 17.Workflows owns these codes privately (WorkflowErrors, prefix "workflow"); the literals below are the expectation.
        new("workflow-timed-out", () => WorkflowErrors.TimedOut().ToException(), ErrorType.Timeout, "workflow.timed_out", 504, "DeadlineExceeded"),
        new("workflow-request-timed-out", () => WorkflowErrors.RequestTimedOut().ToException(), ErrorType.Timeout, "workflow.request_timed_out", 504, "DeadlineExceeded"),
        new("workflow-service-unavailable", () => WorkflowErrors.ServiceUnavailable().ToException(), ErrorType.Unavailable, "workflow.service_unavailable", 503, "Unavailable"),
        new("workflow-not-found", () => WorkflowErrors.NotFound().ToException(), ErrorType.NotFound, "workflow.not_found", 404, "NotFound"),
    ];

    /// <summary>Gets every case of the catalogue.</summary>
    public static IReadOnlyList<EdgeMapperCase> Cases => AllCases;

    /// <summary>Returns the case named <paramref name="name"/>.</summary>
    /// <param name="name">The case name.</param>
    /// <returns>The case.</returns>
    /// <exception cref="InvalidOperationException">No case has that name.</exception>
    public static EdgeMapperCase Find(string name) =>
        AllCases.FirstOrDefault(edgeCase => string.Equals(edgeCase.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No edge-mapper case is named '{name}'.");

    private static EdgeMapperCase Storage(string name, Error error, ErrorType type, int httpStatus, string grpcStatus) =>
        new(name, () => new StorageException(error), type, error.Code, httpStatus, grpcStatus);
}
