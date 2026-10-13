namespace SharedKernel.Workflows.Temporal.Diagnostics;

/// <summary>The operation names logged with a dispatch outcome (the <c>{Operation}</c> placeholder).</summary>
internal static class WorkflowOperations
{
    /// <summary>Starting a workflow.</summary>
    public const string Start = "start";

    /// <summary>Attaching to a workflow by id.</summary>
    public const string GetHandle = "get_handle";

    /// <summary>Describing a workflow.</summary>
    public const string Describe = "describe";

    /// <summary>Signalling a workflow.</summary>
    public const string Signal = "signal";

    /// <summary>Querying a workflow.</summary>
    public const string Query = "query";

    /// <summary>Cancelling a workflow.</summary>
    public const string Cancel = "cancel";

    /// <summary>Terminating a workflow.</summary>
    public const string Terminate = "terminate";

    /// <summary>Awaiting a workflow result.</summary>
    public const string GetResult = "get_result";
}
