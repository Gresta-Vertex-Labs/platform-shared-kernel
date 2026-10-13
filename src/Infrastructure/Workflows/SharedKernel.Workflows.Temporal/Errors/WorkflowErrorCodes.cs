namespace SharedKernel.Workflows.Temporal.Errors;

/// <summary>
/// The stable, wire-visible codes of the <c>17.Workflows</c> dispatch surface, lowercase
/// <c>workflow.{snake_reason}</c>. A caller branches on a code, for example
/// <c>result.Error.Code == WorkflowErrorCodes.AlreadyStarted</c>; the <see cref="SharedKernel.Primitives.Errors.ErrorType"/>
/// of each code is fixed by the package and noted below.
/// </summary>
/// <remarks>
/// A failure raised inside a workflow or activity through <c>ActivityBase.Fail</c> carries the author's own
/// <c>Error.Code</c>, which survives the round trip to the dispatch site unchanged; only the codes below are the
/// package's own.
/// </remarks>
public static class WorkflowErrorCodes
{
    /// <summary>The addressed workflow execution does not exist, or belongs to another tenant (<c>NotFound</c>).</summary>
    public const string NotFound = "workflow.not_found";

    /// <summary>A workflow with the composed id is already running (<c>Conflict</c>).</summary>
    public const string AlreadyStarted = "workflow.already_started";

    /// <summary>The workflow execution completed in a cancelled state (<c>Unexpected</c>).</summary>
    public const string Cancelled = "workflow.cancelled";

    /// <summary>The workflow execution was terminated by an operator (<c>Unexpected</c>).</summary>
    public const string Terminated = "workflow.terminated";

    /// <summary>The workflow execution timed out (<c>Timeout</c>).</summary>
    public const string TimedOut = "workflow.timed_out";

    /// <summary>A dispatch request to the Temporal service ran out of time (<c>Timeout</c>).</summary>
    public const string RequestTimedOut = "workflow.request_timed_out";

    /// <summary>The Temporal service answered with a deployment or contract fault (<c>Unexpected</c>).</summary>
    public const string RpcFailed = "workflow.rpc_failed";

    /// <summary>A query was rejected or could not be answered (<c>Unexpected</c>).</summary>
    public const string QueryFailed = "workflow.query_failed";

    /// <summary>The Temporal service is unavailable (<c>Unavailable</c>).</summary>
    public const string ServiceUnavailable = "workflow.service_unavailable";

    /// <summary>The configured Temporal namespace does not exist on the cluster (<c>Unexpected</c>).</summary>
    public const string NamespaceNotFound = "workflow.namespace_not_found";

    /// <summary>A dispatch call was made with the global tenant scope (<c>Unauthorized</c>).</summary>
    public const string TenantScopeMissing = "workflow.tenant_scope_missing";

    /// <summary>The business key or workflow id is blank (<c>Validation</c>).</summary>
    public const string InvalidWorkflowId = "workflow.invalid_workflow_id";

    /// <summary>The payload codec could not encode or decode a payload (<c>Unexpected</c>).</summary>
    public const string PayloadCodecFailure = "workflow.payload_codec_failure";

    /// <summary>
    /// A Temporal failure the package has no mapping for (<c>Unexpected</c>). Carries a fixed message and no
    /// exception text.
    /// </summary>
    public const string UnmappedFailure = "workflow.unmapped_failure";

    /// <summary>
    /// The code given to an application failure that carries no error type of its own (the original <c>ErrorType</c>
    /// discriminator is still restored from the failure detail when present).
    /// </summary>
    public const string ApplicationFailure = "workflow.application_failure";
}
